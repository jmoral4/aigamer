using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AIGamer
{
    public class ClaudeImageProcessor
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _apiEndpoint;
        private readonly string _visionModel;

        public ClaudeImageProcessor(string apiKey, string apiEndpoint, string visionModel)
        {
            _httpClient = new HttpClient();
            _apiKey = apiKey;
            _apiEndpoint = apiEndpoint;
            _visionModel = visionModel;
        }

        /// <summary>
        /// Captures a window image and processes it using Claude's image understanding capabilities
        /// </summary>
        /// <param name="hWnd">Window handle to capture</param>
        /// <param name="prompt">Optional prompt to give Claude context about the image</param>
        /// <returns>Claude's interpretation of the game screen</returns>
        public async Task<string> GetTextFromWindowImage(IntPtr hWnd, string prompt = null)
        {
            try
            {
                // Capture the window using the existing WindowCapture method
                Bitmap windowImage = WindowCapture.CaptureWindowUsingCopyFromScreen(hWnd);

                // Save the image to a debug file (optional - for debugging)
                windowImage.Save($"claude_image_input_{DateTime.Now:yyyyMMddHHmmss}.png");

                // Convert the bitmap to a base64 string
                string base64Image = BitmapToBase64(windowImage);

                // Clean up bitmap
                windowImage.Dispose();

                // Call Claude API with the image
                string defaultPrompt = "This is a screen from a text-based game. Read all text visible in this image and return it accurately. " +
                                      "Pay special attention to menu options, numbers, and game text. " +
                                      "Format your response to preserve the layout of text as it appears in the game.";

                string userPrompt = string.IsNullOrEmpty(prompt) ? defaultPrompt : prompt;

                return await CallClaudeWithImage(base64Image, userPrompt);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing image with Claude: {ex.Message}");
                return $"ERROR: {ex.Message}";
            }
        }

        private string BitmapToBase64(Bitmap bitmap)
        {
            // Check if we need to resize the image (anthropic has a 20MB limit for requests)
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

        private async Task<string> CallClaudeWithImage(string base64Image, string prompt)
        {
            // Set up the required headers for Claude API
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            // Create message content with the image
            var imageContent = new
            {
                type = "image",
                source = new
                {
                    type = "base64",
                    media_type = "image/png",
                    data = base64Image
                }
            };

            var textContent = new
            {
                type = "text",
                text = prompt
            };

            // Define a content object type to explicitly type the array
            var contentItems = new object[] { textContent, imageContent };

            // Create the full request body
            var requestBody = new
            {
                model = _visionModel, // Use the appropriate model version
                max_tokens = 1000,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = contentItems
                    }
                }
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            // Make the API call
            var response = await _httpClient.PostAsync(_apiEndpoint, content);

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

                    // Extract Claude's text response
                    string claudeResponse = doc.RootElement
                        .GetProperty("content")[0]
                        .GetProperty("text")
                        .GetString();

                    return claudeResponse;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing API response: {ex.Message}");
                Console.WriteLine($"Response content: {responseContent}");
                throw;
            }
        }
    }
}