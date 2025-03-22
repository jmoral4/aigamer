using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AIGamer
{
    public class OllamaImageProcessor
    {
        private readonly HttpClient _httpClient;
        private readonly string _ollamaBaseUrl;
        private readonly string _visionModel;

        public string VisionModel => _visionModel;

        public OllamaImageProcessor(string ollamaBaseUrl = "http://localhost:11435", string visionModel = "gemma3:27b")
        {
            _httpClient = new HttpClient();
            _ollamaBaseUrl = ollamaBaseUrl.TrimEnd('/');
            _visionModel = visionModel;
        }

        /// <summary>
        /// Captures a window image and processes it using Ollama's vision capabilities
        /// </summary>
        /// <param name="hWnd">Window handle to capture</param>
        /// <param name="prompt">Optional prompt to give context about the image</param>
        /// <returns>Ollama's interpretation of the game screen</returns>
        public async Task<string> GetTextFromWindowImage(IntPtr hWnd, string prompt = null)
        {
            try
            {
                // Capture the window using the existing WindowCapture method
                Bitmap windowImage = WindowCapture.CaptureWindowUsingCopyFromScreen(hWnd);

                // Save the image to a debug file (optional - for debugging)
                windowImage.Save($"ollama_image_input_{DateTime.Now:yyyyMMddHHmmss}.png");

                // Convert the bitmap to a base64 string
                string base64Image = BitmapToBase64(windowImage);

                // Clean up bitmap
                windowImage.Dispose();

                // Call Ollama API with the image
                string defaultPrompt = "This is a screen from a text-based game. Read all text visible in this image and return it accurately. " +
                                      "Pay special attention to menu options, numbers, and game text. " +
                                      "Format your response to preserve the layout of text as it appears in the game.";

                string userPrompt = string.IsNullOrEmpty(prompt) ? defaultPrompt : prompt;

                return await CallOllamaWithImage(base64Image, userPrompt);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing image with Ollama: {ex.Message}");
                return $"ERROR: {ex.Message}";
            }
        }

        private string BitmapToBase64(Bitmap bitmap)
        {
            // Check if we need to resize the image (large images may cause issues)
            if (bitmap.Width > 1600 || bitmap.Height > 1600)
            {
                bitmap = ResizeImage(bitmap, 1600);
            }

            using (MemoryStream ms = new MemoryStream())
            {
                // Convert to PNG for better quality
                bitmap.Save(ms, ImageFormat.Png);
                byte[] imageBytes = ms.ToArray();
                return Convert.ToBase64String(imageBytes);
            }
        }

        private Bitmap ResizeImage(Bitmap originalImage, int maxDimension)
        {
            // Calculate the new dimensions while maintaining aspect ratio
            int newWidth, newHeight;

            if (originalImage.Width > originalImage.Height)
            {
                newWidth = maxDimension;
                newHeight = (int)(originalImage.Height * ((float)maxDimension / originalImage.Width));
            }
            else
            {
                newHeight = maxDimension;
                newWidth = (int)(originalImage.Width * ((float)maxDimension / originalImage.Height));
            }

            // Create a new bitmap with the calculated dimensions
            Bitmap resizedImage = new Bitmap(newWidth, newHeight);

            // Draw the original image on the new bitmap with the new dimensions
            using (Graphics g = Graphics.FromImage(resizedImage))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(originalImage, 0, 0, newWidth, newHeight);
            }

            return resizedImage;
        }

        private async Task<string> CallOllamaWithImage(string base64Image, string prompt)
        {
            // Direct Ollama API approach
            string apiUrl = $"{_ollamaBaseUrl}/api/chat";

            // Create the request object with stream set to false
            var requestBody = new
            {
                model = VisionModel,
                stream = false, // Important: disable streaming
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = prompt,
                        images = new[] { base64Image }
                    }
                }
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            // Make the API call
            var response = await _httpClient.PostAsync(apiUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"API call failed with status {response.StatusCode}: {errorContent}");
            }

            string responseContent = await response.Content.ReadAsStringAsync();

            try
            {
                // Parse the JSON response
                using (JsonDocument doc = JsonDocument.Parse(responseContent))
                {
                    // Debug print
                    Console.WriteLine("JSON Response: " + responseContent.Substring(0, Math.Min(300, responseContent.Length)) + "...");

                    // Extract Ollama's text response
                    string ollamaResponse = doc.RootElement
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString();

                    return ollamaResponse;
                }
            }
            catch (Exception ex)
            {
                // If the direct parse fails, attempt to handle the streaming response format
                return HandleStreamingResponse(responseContent);
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
                throw new Exception($"Failed to parse streaming response: {ex.Message}");
            }
        }
    }
}