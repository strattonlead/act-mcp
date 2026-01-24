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
