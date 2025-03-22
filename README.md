# AIGamer

AIGamer is a C# application that enables AI to play the game Warsim automatically. It uses computer vision and AI to analyze the game screen, make decisions, and execute commands by simulating keyboard input.

## Demo

Watch AIGamer in action:

[![AIGamer Demo](https://img.youtube.com/vi/yU4_c-OXhMY/0.jpg)](https://www.youtube.com/watch?v=yU4_c-OXhMY)

## Features

- **AI Integration**: Support for multiple AI providers:
  - Anthropic (Claude)
  - Ollama (local models)
  - Extensible architecture with placeholders for OpenAI and Gemini
  
- **Game Control**:
  - Automatically finds and focuses the Warsim game window
  - Captures the game screen and analyzes it using AI vision models
  - Generates appropriate game actions based on the current state
  - Executes commands through simulated keyboard input
  
- **User Controls**:
  - Pause/Resume the AI's actions
  - Step mode for granular control (execute one action at a time)
  - Adjustable speed controls
  - Session logging for review
  
- **Configuration**:
  - Easy setup through configuration file
  - Customizable AI providers and models
  
- **Extensibility**:
  - While currently configured for Warsim, the architecture can theoretically work with any game that is:
    - Slow-paced
    - Turn-based
    - Preferably CLI-based
  - With modifications to the core prompt, it could also work with graphical games like poker

## Installation

1. Clone this repository
2. Copy `appsettings.example.json` to `appsettings.json`
3. Add your API keys and configure your preferred AI provider in `appsettings.json`
4. Build and run the project

## Configuration

Before running the application, you must:

1. **Create configuration file**: Copy the example configuration file and rename it: `appsettings.json`

2. **Configure AI provider**: Edit `appsettings.json` to set your preferred AI provider and add your API key (if applicable). The configuration supports:
- Anthropic (Claude)
- Ollama (local models)
- OpenAI (placeholder)
- Gemini (placeholder)

## Usage

1. Start the Warsim game
2. Launch AIGamer
3. Enter an optional session name (for logging purposes)
4. Press any key to begin when ready

## Controls

The following keyboard controls are available while the AI is playing:

- **ESC** - Exit program
- **P** - Pause/Resume AI actions
- **S** - Toggle step mode (pause after each action)
- **N** - Execute next action (when paused/in step mode)
- **+** - Increase speed (reduce delay between actions)
- **-** - Decrease speed (increase delay between actions)
- **R** - Reset speed to default (5 seconds)
- **H** - Display controls help

## Requirements

- Visual Studio 2022
- .NET 8.0
- Warsim game installed and runnable
- (optional) API keys for cloud-based AI providers (if not using Ollama)
- Ollama and either a multi-modal model (Gemma3 > 14B) or both a language model (the brains) and a vision model (the eyes -- llama3.2-vision)

## Logging

The application logs all AI actions and system events /logs/ in your running directory. The log file path is displayed when the application starts. All images captured are also stored in the application directory and named for the model and date of capture.

## Other
There is a module for processing OCR via Tesseract instead of AI. It would require a slight change to flow but was left in the codebase. It is no longer used since it was generating poor results for larger screens full of options. I also realized that having vision models process the images would allow for more than just CLI based games in the future.
