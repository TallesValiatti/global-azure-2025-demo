namespace OverviewAzureAiAgentService.Api.Services.Models;

public record CreateAgentRequest(
    string Name, 
    string Instructions,
    bool IsDocAgent = false);