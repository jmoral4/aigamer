using System;

namespace AIGamer
{
    // Interface for AI game controllers
    public interface IAIGameController
    {
        string ModelName { get; }
        string VisionModel { get; }
        Task<string> GetGameActionFromScreen(IntPtr gameWindow, SessionLogger logger);
        GameCommand ParseGameCommand(string action);
    }

    // Settings class for AI configuration
    public class AISettings
    {
        public string Provider { get; set; } = "Ollama"; // Default provider
        public AnthropicSettings Anthropic { get; set; } = new AnthropicSettings();
        public OpenAISettings OpenAI { get; set; } = new OpenAISettings();
        public GeminiSettings Gemini { get; set; } = new GeminiSettings();
        public OllamaSettings Ollama { get; set; } = new OllamaSettings();
    }

    public class AnthropicSettings
    {
        public string ApiKey { get; set; } = "";
        public string ApiEndpoint { get; set; } = "https://api.anthropic.com/v1/messages";
        public string Model { get; set; } = "claude-3-opus-20240229";
        public string VisionModel { get; set; } = "Claude Vision";
    }

    public class OpenAISettings
    {
        public string ApiKey { get; set; } = "";
        public string ApiEndpoint { get; set; } = "https://api.openai.com/v1/chat/completions";
        public string Model { get; set; } = "gpt-4";
        public string VisionModel { get; set; } = "GPT-4 Vision";
    }

    public class GeminiSettings
    {
        public string ApiKey { get; set; } = "";
        public string ApiEndpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/models/gemini-pro:generateContent";
        public string Model { get; set; } = "gemini-pro";
        public string VisionModel { get; set; } = "Gemini Vision";
    }

    public class OllamaSettings
    {
        public string ApiEndpoint { get; set; } = "http://localhost:11434/api/generate";
        public string Model { get; set; } = "llama3";
        public string VisionModel { get; set; } = "llava";
    }
}