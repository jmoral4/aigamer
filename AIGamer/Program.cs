using AIGamer;
using Microsoft.Extensions.Configuration;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;

namespace AIGamer;

class Program
{
    // Configuration
    private static AISettings _aiSettings;
    private const int MaxRetries = 3;
    public static readonly object ConsoleLock = new object();

    // Speed control settings
    private static int _gameLoopDelayMs = 2000; // Default delay between actions
    private static bool _isPaused = false;
    private static bool _stepMode = false;

    static async Task Main(string[] args)
    {
        Console.WriteLine("AI Warsim Player Starting...");
        Console.WriteLine("Please make sure Warsim is running before continuing.");

        // Load configuration from appsettings.json
        try
        {
            var configuration = new ConfigurationBuilder()                
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // Load AI settings
            _aiSettings = new AISettings();
            configuration.GetSection("AI").Bind(_aiSettings);

            Console.WriteLine($"Using AI provider: {_aiSettings.Provider}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading configuration: {ex.Message}");
            Console.WriteLine("Please ensure appsettings.json exists and is properly formatted.");
            return;
        }

        // Get optional session name for logging
        Console.WriteLine("Enter a name for this session (optional, press Enter to skip):");
        string sessionName = Console.ReadLine();

        // Initialize the logger
        SessionLogger logger = new SessionLogger(sessionName);
        Console.WriteLine($"Logging to: {logger.GetLogFilePath()}");
        logger.LogSystemEvent("AI Warsim Player initialized");

        Console.WriteLine("Press any key when ready to start...");
        Console.ReadKey();

        // Find the game window and process
        GameWindowFinder windowFinder = new GameWindowFinder();
        IntPtr gameWindow = windowFinder.FindWarsimWindow();

        if (gameWindow == IntPtr.Zero)
        {
            Console.WriteLine("Could not find Warsim window. Make sure the game is running.");
            logger.LogError("Could not find Warsim window");
            return;
        }

        Console.WriteLine("Found Warsim window!");
        logger.LogSystemEvent("Found Warsim window");

        // Get the process ID from the window handle
        uint processId;
        GetWindowThreadProcessId(gameWindow, out processId);

        if (processId == 0)
        {
            Console.WriteLine("Could not get process ID for Warsim.");
            logger.LogError("Could not get process ID for Warsim");
            return;
        }

        Process gameProcess = Process.GetProcessById((int)processId);
        Console.WriteLine($"Found Warsim process (ID: {processId}, Name: {gameProcess.ProcessName})");
        logger.LogSystemEvent($"Found Warsim process (ID: {processId}, Name: {gameProcess.ProcessName})");

        KeyboardInputManager inputManager = new KeyboardInputManager(gameWindow);

        // Create the appropriate AI controller based on settings
        IAIGameController aiController = CreateAIController(_aiSettings);
        logger.LogSystemEvent($"Using {_aiSettings.Provider} AI model: {aiController.ModelName}");

        // Display controls
        PrintControls();

        // Start key monitoring in a separate task
        Task keyMonitorTask = Task.Run(() => MonitorKeyboardInput(logger));

        // Allow user to see what happens
        Console.WriteLine($"{aiController.ModelName} will now begin playing. Use controls to adjust speed or pause.");
        logger.LogSystemEvent($"{aiController.ModelName} play started");
        Thread.Sleep(1000);

        // Main game loop
        bool running = true;
        while (running)
        {
            try
            {
                // Check if we should exit (this will be set by the key monitor)
                if (!running)
                {
                    break;
                }

                // Handle pause state
                if (_isPaused)
                {
                    Console.WriteLine("Game is paused. Press 'P' to resume or 'N' for next step.");
                    await Task.Delay(500);
                    continue;
                }

                // In step mode, wait for user to press 'N'
                if (_stepMode)
                {
                    Console.WriteLine("Step mode active. Press 'N' for next action.");
                    _isPaused = true;
                    await Task.Delay(500);
                    continue;
                }

                // Focus the game window first to ensure it's the active window
                if (!inputManager.FocusGameWindow())
                {
                    Console.WriteLine("Warning: Failed to focus game window before capturing. Retrying...");
                    Thread.Sleep(1000);
                    continue;
                }

                // Give the window focus time to take effect
                Thread.Sleep(500);

                // Get AI action directly from the screen image
                Console.WriteLine($"Capturing game screen using {aiController.VisionModel} vision...");
                Console.WriteLine("Using AI to analyze game screen and decide next action...");
                string action = await aiController.GetGameActionFromScreen(gameWindow, logger);

                if (string.IsNullOrWhiteSpace(action) || action == "ERROR")
                {
                    Console.WriteLine("Warning: Failed to get AI action from screen. Retrying...");
                    logger.LogError("Failed to get AI action from screen");
                    Thread.Sleep(1000);
                    continue;
                }

                // Parse the action
                GameCommand command = aiController.ParseGameCommand(action);

                // Log the AI action
                logger.LogAIAction(command.CommandValue, action);

                // Execute the command
                Console.WriteLine($"Executing command: {command.CommandType} - {command.CommandValue}");

                int retries = 0;
                bool actionExecuted = false;

                while (!actionExecuted && retries < MaxRetries)
                {
                    try
                    {
                        if (!inputManager.FocusGameWindow())
                        {
                            Console.WriteLine("Warning: Failed to focus game window. Retrying...");
                            logger.LogError("Failed to focus game window");
                            Thread.Sleep(1000);
                            retries++;
                            continue;
                        }

                        switch (command.CommandType)
                        {
                            case GameCommandType.Key:
                                // Send single key press
                                inputManager.SendKey(command.CommandValue[0]);
                                inputManager.SendEnter();
                                break;

                            case GameCommandType.Text:
                                // Send text string
                                inputManager.SendText(command.CommandValue);
                                inputManager.SendEnter();
                                break;

                            case GameCommandType.ArrowKey:
                                // Send arrow key
                                ArrowDirection direction = ArrowDirection.Up; // Default

                                if (command.CommandValue.Contains("UP"))
                                    direction = ArrowDirection.Up;
                                else if (command.CommandValue.Contains("DOWN"))
                                    direction = ArrowDirection.Down;
                                else if (command.CommandValue.Contains("LEFT"))
                                    direction = ArrowDirection.Left;
                                else if (command.CommandValue.Contains("RIGHT"))
                                    direction = ArrowDirection.Right;

                                inputManager.SendArrowKey(direction);
                                break;

                            default:
                                Console.WriteLine("Unknown command type. Sending as text.");
                                inputManager.SendText(command.CommandValue);
                                inputManager.SendEnter();
                                break;
                        }

                        actionExecuted = true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error executing action: {ex.Message}. Retrying...");
                        logger.LogError($"Error executing action: {ex.Message}");
                        retries++;
                        Thread.Sleep(1000);
                    }
                }

                // Wait before next action to see results
                Console.WriteLine($"Waiting {_gameLoopDelayMs / 1000} seconds before next action...");
                await Task.Delay(_gameLoopDelayMs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in game loop: {ex.Message}");
                logger.LogError($"Error in game loop: {ex.Message}");
                await Task.Delay(2000);
            }
        }

        Console.WriteLine("AI player stopped. Press any key to exit.");
        logger.LogSystemEvent("AI player stopped");
        Console.ReadKey();
    }

    // Factory method to create the appropriate AI controller based on settings
    private static IAIGameController CreateAIController(AISettings settings)
    {
        switch (settings.Provider.ToLower())
        {
            case "anthropic":
                return new AnthropicGameController(
                    settings.Anthropic.ApiKey,
                    settings.Anthropic.ApiEndpoint, settings.Anthropic.Model, settings.Anthropic.VisionModel);            
            case "ollama":            
                return new OllamaGameController(
                    settings.Ollama.ApiEndpoint, settings.Ollama.Model, settings.Ollama.VisionModel );
            //case "openai":
            //    return new OpenAIGameController(
            //        settings.OpenAI.ApiKey,
            //        settings.OpenAI.ApiEndpoint);

            //case "gemini":
            //    return new GeminiGameController(
            //        settings.Gemini.ApiKey,
            //        settings.Gemini.ApiEndpoint);
            default:
                return null;
        }
    }

    private static void PrintControls()
    {
        Console.WriteLine("\n=== CONTROLS ===");
        Console.WriteLine("ESC - Exit program");
        Console.WriteLine("P - Pause/Resume AI actions");
        Console.WriteLine("S - Toggle step mode (pause after each action)");
        Console.WriteLine("N - Execute next action (when paused/in step mode)");
        Console.WriteLine("+ - Increase speed (reduce delay)");
        Console.WriteLine("- - Decrease speed (increase delay)");
        Console.WriteLine("R - Reset speed to default");
        Console.WriteLine("==============\n");
    }

    private static void MonitorKeyboardInput(SessionLogger logger)
    {
        while (true)
        {
            lock (ConsoleLock)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);

                    switch (key.Key)
                    {
                        case ConsoleKey.Escape:
                            logger.LogSystemEvent("User pressed ESC - exiting program");
                            Environment.Exit(0);
                            break;

                        case ConsoleKey.P:
                            _isPaused = !_isPaused;
                            Console.WriteLine(_isPaused ? "Game PAUSED" : "Game RESUMED");
                            logger.LogSystemEvent(_isPaused ? "Game PAUSED by user" : "Game RESUMED by user");
                            break;

                        case ConsoleKey.S:
                            _stepMode = !_stepMode;
                            Console.WriteLine(_stepMode ? "Step mode ENABLED" : "Step mode DISABLED");
                            logger.LogSystemEvent(_stepMode ? "Step mode ENABLED by user" : "Step mode DISABLED by user");
                            if (!_stepMode)
                            {
                                _isPaused = false;
                            }
                            break;

                        case ConsoleKey.N:
                            if (_isPaused)
                            {
                                Console.WriteLine("Executing next step...");
                                logger.LogSystemEvent("User triggered next step");
                                _isPaused = _stepMode; // Stay paused only if in step mode
                            }
                            break;

                        case ConsoleKey.Add:
                        case ConsoleKey.OemPlus:
                            _gameLoopDelayMs = Math.Max(500, _gameLoopDelayMs - 1000);
                            Console.WriteLine($"Speed increased: {_gameLoopDelayMs / 1000} seconds between actions");
                            logger.LogSystemEvent($"Speed increased to {_gameLoopDelayMs / 1000} seconds between actions");
                            break;

                        case ConsoleKey.Subtract:
                        case ConsoleKey.OemMinus:
                            _gameLoopDelayMs = Math.Min(30000, _gameLoopDelayMs + 1000);
                            Console.WriteLine($"Speed decreased: {_gameLoopDelayMs / 1000} seconds between actions");
                            logger.LogSystemEvent($"Speed decreased to {_gameLoopDelayMs / 1000} seconds between actions");
                            break;

                        case ConsoleKey.R:
                            _gameLoopDelayMs = 5000;
                            Console.WriteLine("Speed reset to default (5 seconds)");
                            logger.LogSystemEvent("Speed reset to default (5 seconds)");
                            break;

                        case ConsoleKey.H:
                            PrintControls();
                            break;
                    }
                }
            }
            Thread.Sleep(100); // Small delay to avoid hogging CPU
        }
    }

    // Add this method for getting the process ID from a window handle
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}