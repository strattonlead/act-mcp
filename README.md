# ACT-MCP

## Overview
ACT-MCP is an application that integrates **Affect Control Theory (ACT)** with the **Model Context Protocol (MCP)**. It leverages **ASP.NET Core** for the backend, **R** for statistical analytics and ACT calculations, and **MongoDB** for data persistence.


## Technologies
- **ASP.NET Core** (Backend)
- **R** (Analytics via `ahcombs/actdata`, `ahcombs/bayesactR`, `ekmaloney/inteRact`)
- **MongoDB** (Database)
- **Docker** (Containerization)

## Inspiration & References

This project is inspired by **Affect Control Theory (ACT)** and the **Interact** software.

-   **Original Project Website**: [Interact - Affect Control Theory](https://www.indiana.edu/~socpsy/ACT/index.htm)
-   **Reference Guide**: [InteractGuide.pdf](ACT/InteractGuide.pdf)

## Environment Variables

The following environment variables are required to configure the application:

| Variable | Description |
|----------|-------------|
| `OPENAI_API_KEY` | API key for OpenAI services. |
| `OLLAMA_ENDPOINT` | Endpoint URL for Ollama (local LLM). |
| `OPENAI_CHAT_MODEL` | The specific OpenAI chat model to use. |
| `S3_ENDPOINT` | Endpoint URL for S3-compatible storage. |
| `S3_ACCESS_KEY` | Access key for S3. |
| `S3_SECRET_KEY` | Secret key for S3. |
| `S3_BUCKET_NAME` | Name of the S3 bucket. |
| `MONGODB_CONNECTION_STRING` | Connection string for the MongoDB instance. |
| `MONGODB_DATABASE_NAME` | Name of the MongoDB database. |
| `USE_HTTPS` | Boolean flag to enable/disable HTTPS. |
| `BASE_URL` | Base domain/URL of the application (e.g., `api.myserver.com`), used for constructing MCP links. |

## LLM Requirements

To generate high-quality results with the Auto Evaluator, an LLM with a **sufficiently large context window** is required. The agent includes full cultural dictionaries (which can range from hundreds to thousands of entries) in the system prompt to ensure strict adherence to valid identities and behaviors.

**Recommended Models:**
-   **GPT-4o** (Excellent instruction following & large context)
-   **Claude 3.5 Sonnet** (Excellent instruction following & large context)
-   **Gemini 1.5 Pro** (Huge context window, great for large dictionaries)

**Minimum Viable Models (Assumptions for "Small but Capable"):**
-   **Llama 3.1 8B** (128k context): Likely the smallest open-weights model capable of handling the large context and strict JSON formatting instructions.
-   **Mistral NeMo 12B** (128k context): Another strong contender for a smaller footprint model with large context support.
-   **Gemini 1.5 Flash**: Very efficient, large context, and capable reasoning.

> **Note**: Smaller models with limited context windows (e.g., basic 4k/8k models) will likely fail or truncate the dictionary, leading to invalid identity generation.

## Getting Started

1.  Ensure you have **Docker** installed.
2.  Set up your `.env` file with the variables listed above.
3.  Build and run the Docker container:
    ```bash
    docker build -t act-mcp .
    docker run --env-file .env -p 8080:80 act-mcp
    ```


## Docker

The Docker image is available at:
`createiflabs/act-mcp:latest`

## MCP Endpoints

-   **MCP Server Endpoint**: `/mcp` (e.g., `http://localhost:8080/mcp`)
-   **Live Usage View**: `/mcp-usage` (e.g., `http://localhost:8080/mcp-usage`)

## Development

For local development:
1.  Ensure **.NET SDK** and **R** are installed.
2.  Install required R packages.
3.  Run the application using the .NET CLI or Visual Studio.
