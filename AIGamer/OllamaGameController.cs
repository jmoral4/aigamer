using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AIGamer
{
    public class OllamaGameController : IAIGameController
    {
        private readonly HttpClient _httpClient;
        private readonly string _ollamaBaseUrl;
        private readonly string _modelName;
        private List<MessageEntry> _conversationHistory;
        private string _systemPrompt;
        private readonly OllamaImageProcessor _imageProcessor;
        private const int MaxHistoryMessages = 20; // Adjust based on model context limits

        public string ModelName => _modelName;
        public string VisionModel { get; set; }

        public OllamaGameController(string ollamaBaseUrl = "http://localhost:11435", string modelName = "gemma3:27b", string visionModel = "gemma3:27b")
        {
            _httpClient = new HttpClient();
            _ollamaBaseUrl = ollamaBaseUrl.TrimEnd('/');
            _modelName = modelName;
            VisionModel = visionModel;
            _conversationHistory = new List<MessageEntry>();
            _imageProcessor = new OllamaImageProcessor(ollamaBaseUrl, visionModel);

            // Set system prompt
            _systemPrompt = "You are playing the text-based game Warsim: The Realm of Aslona. " +
                            "Analyze the game text and respond with a single game decision to take. " +
                            "Your response should be only the number or thing requested. " +
                            "Valid actions include entering a number choice, typing 'y' or 'n' for yes/no questions, " +
                            "or entering text for name fields. " +
                            "Focus on exploration and making interesting choices in the game. " +
                            "DO NOT explain your reasoning or provide additional text. ONLY respond with a valid action. " +
                            "IMPORTANT: Due to a bug in OCR, menus will often show 8 instead of 0 for the last item. 0 is usually for Exiting, Going back, accepting or taking action. It's never an 'option'. If you find yourself stuck, try 0.";

            // Add system prompt as the first entry in conversation history so it's never removed
            _conversationHistory.Add(new MessageEntry
            {
                Role = "system",
                Content = _systemPrompt
            });
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
                var response = await CallOllamaApi(gameState);

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

        // Method to process game screen image
        public async Task<string> GetGameActionFromScreen(IntPtr gameWindowHandle, SessionLogger logger = null)
        {
            try
            {
                // Use Ollama to process the game screen image
                string gameText = await _imageProcessor.GetTextFromWindowImage(
                    gameWindowHandle,
                    "This is a text-based game screen. Read all visible text and return it exactly as shown."
                );

                // Log the extracted text
                logger?.LogMessage($"Ollama extracted text from screen:\n{gameText}");

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
            if (_conversationHistory.Count > MaxHistoryMessages + 1) // +1 to account for the system message
            {
                // Create a summary of recent game context
                string contextSummary = CreateGameContextSummary();

                // Remove older messages, but keep the system message
                int messagesToRemove = _conversationHistory.Count - (MaxHistoryMessages / 2) - 1; // -1 for the system message
                _conversationHistory.RemoveRange(1, messagesToRemove); // Start from index 1 to keep the system message

                // Add the context summary after the system message
                _conversationHistory.Insert(1, new MessageEntry
                {
                    Role = "user",
                    Content = contextSummary
                });

                // Add a placeholder assistant response
                _conversationHistory.Insert(2, new MessageEntry
                {
                    Role = "assistant",
                    Content = "Understood. I'll continue playing based on the current game state."
                });
            }
        }

        private string CreateGameContextSummary()
        {
            // Create a summary of recent game context
            StringBuilder summary = new StringBuilder("PREVIOUS GAME CONTEXT:\n");

            // Go through the history to find recent exchanges (skip the system message)
            for (int i = 1; i < _conversationHistory.Count - 2; i += 2)
            {
                if (i + 1 < _conversationHistory.Count &&
                    _conversationHistory[i].Role == "user" &&
                    _conversationHistory[i + 1].Role == "assistant")
                {
                    string gameStateMsg = _conversationHistory[i].Content;
                    string action = _conversationHistory[i + 1].Content;

                    // Extract a brief snippet of the game state
                    string gameStateSnippet = ExtractGameStateSnippet(gameStateMsg);

                    summary.AppendLine($"Game showed: {gameStateSnippet}");
                    summary.AppendLine($"You chose: {action}\n");

                    // Limit to 2 recent exchanges for brevity
                    if (i >= 5) // We've added 2 exchanges
                        break;
                }
            }

            summary.AppendLine("Some older history has been removed to save space. Continue making decisions based on the current game state.");

            return summary.ToString();
        }

        private string ExtractGameStateSnippet(string fullStateMsg)
        {
            // Extract a concise snippet from a game state message
            if (!fullStateMsg.Contains("Current game state:"))
                return fullStateMsg;

            // Extract the part between "Current game state:" and "What action should I take next?"
            int startIndex = fullStateMsg.IndexOf("Current game state:") + "Current game state:".Length;
            int endIndex = fullStateMsg.IndexOf("What action should I take next?");

            if (endIndex == -1) // If the end marker isn't found
                endIndex = fullStateMsg.Length;

            string gameState = fullStateMsg.Substring(startIndex, endIndex - startIndex).Trim();

            // Keep it concise
            string[] lines = gameState.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 2)
            {
                return string.Join(" | ", lines.Take(2).Select(l => l.Trim())) + "...";
            }

            return gameState;
        }

        private async Task<string> CallOllamaApi(string gameState)
        {
            // Update conversation history with current game state
            UpdateConversationHistory(gameState);

            // Now the system message is already part of the conversation history,
            // so we can just use the history directly
            var messages = _conversationHistory.Select(msg => new
            {
                role = msg.Role,
                content = msg.Content
            }).ToList();

            var requestBody = new
            {
                model = ModelName,
                messages = messages,
                stream = false, // Important: disable streaming
                max_tokens = 150,
                temperature = 0.7
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            // Make the API call to Ollama
            var response = await _httpClient.PostAsync($"{_ollamaBaseUrl}/api/chat", content);

            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"API call failed with status {response.StatusCode}: {errorContent}");
            }

            string responseContent = await response.Content.ReadAsStringAsync();

            try
            {
                // Parse the JSON response from Ollama
                using (JsonDocument doc = JsonDocument.Parse(responseContent))
                {
                    // Print the first part of the response for debugging
                    Console.WriteLine("JSON Response: " + responseContent.Substring(0, Math.Min(100, responseContent.Length)) + "...");

                    // Ollama API response format
                    string aiResponse = doc.RootElement
                        .GetProperty("message")
                        .GetProperty("content")
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

                // Try to handle streaming response format
                string content2 = HandleStreamingResponse(responseContent);

                // Add the AI's response to our conversation history
                if (!string.IsNullOrEmpty(content2))
                {
                    _conversationHistory.Add(new MessageEntry
                    {
                        Role = "assistant",
                        Content = content2
                    });
                }

                return content2;
            }
        }

        private string HandleStreamingResponse(string responseContent)
        {
            StringBuilder fullContent = new StringBuilder();

            try
            {
                // Split the response by newlines as each line is a separate JSON object
                string[] jsonLines = responseContent.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries
                );

                // Process each JSON object
                foreach (string jsonLine in jsonLines)
                {
                    using (JsonDocument doc = JsonDocument.Parse(jsonLine))
                    {
                        // Check if this JSON object has a message with content
                        if (doc.RootElement.TryGetProperty("message", out JsonElement messageElement) &&
                            messageElement.TryGetProperty("content", out JsonElement contentElement))
                        {
                            string contentPiece = contentElement.GetString();
                            fullContent.Append(contentPiece);
                        }
                    }
                }

                return fullContent.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling streaming response: {ex.Message}");
                return "ERROR: Failed to parse streaming response";
            }
        }

        // Same ParseActionFromResponse and ParseGameCommand methods as before...
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
}