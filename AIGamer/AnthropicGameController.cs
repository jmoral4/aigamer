using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AIGamer
{
    public class AnthropicGameController : IAIGameController
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _apiEndpoint;
        private List<MessageEntry> _conversationHistory;
        private string _systemPrompt;
        private readonly ClaudeImageProcessor _imageProcessor;
        private const int MaxHistoryMessages = 20; // Adjust based on API limits
        public string ModelName { get; set; }
        public string VisionModel { get; set; }

        public AnthropicGameController(string apiKey, string apiEndpoint, string model, string visionModel)
        {
            _httpClient = new HttpClient();
            _apiKey = apiKey;
            _apiEndpoint = apiEndpoint;
            _conversationHistory = new List<MessageEntry>();
            VisionModel = visionModel;
            ModelName = model;
            _imageProcessor = new ClaudeImageProcessor(apiKey, apiEndpoint, visionModel);

            // Set system prompt as a separate variable (not as a message in the history)
            _systemPrompt = "You are playing the text-based game Warsim: The Realm of Aslona. " +
                         "Analyze the game text and respond with a single game decision to take. " +
                         "Your response should be only the number or thing requested." +
                         "Valid actions include entering a number choice, typing 'y' or 'n' for yes/no questions, " +
                         "or entering text for name fields. " +
                         "Focus on exploration and making interesting choices in the game. " +
                         "DO NOT explain your reasoning or provide additional text. ONLY respond with a valid action. " +
                         "BEWARE: Due to a bug in OCR, menus will often confuse 8 and 0. 0 is usually for Exiting, Going back, or Accepting. It's never an 'option'. If you find yourself unable to proceed when pressing 8, try 0.";
        }

        // Message structure for API
        private class MessageEntry
        {
            public string Role { get; set; }
            public string Content { get; set; }
        }

        public async Task<string> GetGameAction(string gameState, SessionLogger logger = null)
        {
            try
            {
                // Call the AI API (this now handles updating history internally)
                var response = await CallAIApi(gameState);

                // Log the full response if logger is provided
                logger?.LogMessage($"Full AI response:\n{response}");

                // Parse the action from the AI response
                return ParseActionFromResponse(response);
            }
            catch (Exception ex)
            {
                logger?.LogError($"Error getting game action: {ex.Message}");
                Console.WriteLine($"Error getting game action: {ex.Message}");
                return "ERROR";
            }
        }

        // New method to process game screen image
        public async Task<string> GetGameActionFromScreen(IntPtr gameWindowHandle, SessionLogger logger = null)
        {
            try
            {
                // Use Claude to process the game screen image
                string gameText = await _imageProcessor.GetTextFromWindowImage(
                    gameWindowHandle,
                    "This is a text-based game screen. Read all visible text and return it exactly as shown."
                );

                // Log the extracted text
                logger?.LogMessage($"Claude extracted text from screen:\n{gameText}");

                // Now use this text as input for game decision
                return await GetGameAction(gameText, logger);
            }
            catch (Exception ex)
            {
                logger?.LogError($"Error processing game screen: {ex.Message}");
                Console.WriteLine($"Error processing game screen: {ex.Message}");
                return "ERROR";
            }
        }

        private void UpdateConversationHistory(string gameState)
        {
            // Add the current game state as a user message
            _conversationHistory.Add(new MessageEntry
            {
                Role = "user",
                Content = $"Current game state:\n{gameState}\n\nWhat action should I take next?"
            });

            // Manage history length - keep only the most recent exchanges
            if (_conversationHistory.Count > MaxHistoryMessages)
            {
                // Remove the oldest user/assistant exchange (2 messages)
                _conversationHistory.RemoveRange(0, 2);
            }
        }

        private async Task<string> CallAIApi(string gameState)
        {
            // Update conversation history with current game state
            UpdateConversationHistory(gameState);

            // Convert our message history to the format expected by Claude API
            var messages = _conversationHistory.Select(msg => new
            {
                role = msg.Role,
                content = msg.Content
            }).ToArray();

            var requestBody = new
            {
                model = ModelName, // Using latest model
                messages = messages,
                system = _systemPrompt,
                max_tokens = 150,
                temperature = 0.7
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            // Set up the required headers for Claude API
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var response = await _httpClient.PostAsync(_apiEndpoint, content);

            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"API call failed with status {response.StatusCode}: {errorContent}");
            }

            string responseContent = await response.Content.ReadAsStringAsync();

            try
            {
                // Parse the JSON response - Claude's API format
                using (JsonDocument doc = JsonDocument.Parse(responseContent))
                {
                    // Print the first part of the response for debugging
                    Console.WriteLine("JSON Response: " + responseContent.Substring(0, Math.Min(100, responseContent.Length)) + "...");

                    // Claude API response format
                    string aiResponse = doc.RootElement
                        .GetProperty("content")[0]
                        .GetProperty("text")
                        .GetString();

                    // Add the AI's response to our conversation history
                    _conversationHistory.Add(new MessageEntry
                    {
                        Role = "assistant",
                        Content = aiResponse
                    });

                    return aiResponse;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing API response: {ex.Message}");
                Console.WriteLine($"Response content: {responseContent}");
                throw;
            }
        }

        private string ParseActionFromResponse(string aiResponse)
        {
            // Extract the action from the AI's response
            Console.WriteLine($"Raw AI response: {aiResponse}");

            if (string.IsNullOrWhiteSpace(aiResponse))
            {
                Console.WriteLine("Warning: Received empty response from AI");
                return "ERROR";
            }

            if (aiResponse.Contains("ACTION:", StringComparison.OrdinalIgnoreCase))
            {
                int actionIndex = aiResponse.IndexOf("ACTION:", StringComparison.OrdinalIgnoreCase);
                string action = aiResponse.Substring(actionIndex + 7).Trim();

                // Clean up the action - remove quotes or other formatting
                action = action.Trim('\"', '\'', '`', ' ', '\n', '\r');

                // If the action has multiple lines, just take the first one
                if (action.Contains("\n"))
                {
                    action = action.Substring(0, action.IndexOf('\n')).Trim();
                }

                Console.WriteLine($"AI chose action: {action}");
                return action;
            }

            // If no explicit ACTION format, try to extract a simple command
            // Remove any explanations and just take what looks like a command
            string[] lines = aiResponse.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                string trimmedLine = line.Trim();
                if (trimmedLine.Length > 0 && trimmedLine.Length < 20 && !trimmedLine.EndsWith('.'))
                {
                    Console.WriteLine($"Extracted potential action: {trimmedLine}");
                    return trimmedLine;
                }
            }

            // If all else fails, return the first line or a short portion of the response
            string fallbackAction = aiResponse.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)[0];
            if (fallbackAction.Length > 20)
            {
                fallbackAction = fallbackAction.Substring(0, 20);
            }

            Console.WriteLine($"Couldn't parse action, using fallback: {fallbackAction}");
            return fallbackAction;
        }

        // Method to handle special action commands
        public GameCommand ParseGameCommand(string action)
        {
            GameCommand command = new GameCommand
            {
                CommandType = GameCommandType.Unknown,
                CommandValue = action
            };

            // Trim and normalize the action string
            action = action.Trim().ToUpper();

            // Check for arrow keys
            if (action == "UP" || action == "ARROW UP")
            {
                command.CommandType = GameCommandType.ArrowKey;
                command.CommandValue = "UP";
            }
            else if (action == "DOWN" || action == "ARROW DOWN")
            {
                command.CommandType = GameCommandType.ArrowKey;
                command.CommandValue = "DOWN";
            }
            else if (action == "LEFT" || action == "ARROW LEFT")
            {
                command.CommandType = GameCommandType.ArrowKey;
                command.CommandValue = "LEFT";
            }
            else if (action == "RIGHT" || action == "ARROW RIGHT")
            {
                command.CommandType = GameCommandType.ArrowKey;
                command.CommandValue = "RIGHT";
            }
            // Check for single characters (like 'y', 'n', or number choices)
            else if (action.Length == 1 || int.TryParse(action, out _))
            {
                command.CommandType = GameCommandType.Key;
            }
            // Anything else is considered text input
            else
            {
                command.CommandType = GameCommandType.Text;
            }

            return command;
        }
    }

    public enum GameCommandType
    {
        Unknown,
        Key,     // Single key press (e.g., 'y', 'n', '1', '2')
        Text,    // Text to type (e.g., character name)
        ArrowKey // Arrow key press
    }

    public class GameCommand
    {
        public GameCommandType CommandType { get; set; }
        public string CommandValue { get; set; }
    }
}