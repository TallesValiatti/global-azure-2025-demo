using System.Text;
using Azure;
using Azure.AI.Projects;
using Azure.Identity;
using OverviewAzureAiAgentService.Api.Services.Models;
using Agent = OverviewAzureAiAgentService.Api.Services.Models.Agent;
using Thread = OverviewAzureAiAgentService.Api.Services.Models.Thread;

namespace OverviewAzureAiAgentService.Api.Services;

public class AgentService(IConfiguration configuration)
{
    private string _annotationMark = "📖";
    
    private AgentsClient CreateAgentsClient()
    {
        var connectionString = configuration["AiServiceProjectConnectionString"]!;
        return new AgentsClient(connectionString, new DefaultAzureCredential());
    }

    public async Task<Agent> CreateAgentAsync(CreateAgentRequest request)
    {
        var aiModel = configuration["AiModel"]!;
        var client = CreateAgentsClient();

        var agentResponse = await client.CreateAgentAsync(
            model: aiModel,
            name: request.Name,
            instructions: request.Instructions);

        return new Agent(
            agentResponse.Value.Id,
            agentResponse.Value.Name,
            agentResponse.Value.Instructions);
    }

    public async Task<Agent> CreateDocAgentAsync(CreateAgentRequest request)
    {
        var aiModel = configuration["AiModel"]!;
        var client = CreateAgentsClient();
        
        var fileIds = new List<string>();

        fileIds.Add(await UploadFileAsync(Constants.FileSearchDoc, $"{nameof(Constants.FileSearchDoc)}.txt"));
        fileIds.Add(await UploadFileAsync(Constants.ModelSupportDoc, $"{nameof(Constants.ModelSupportDoc)}.txt"));
        
        var vectorStoreId = await CreateDocVectorStoreAsync(files: fileIds); 
        
        var fileSearchToolResource = new FileSearchToolResource();
        fileSearchToolResource.VectorStoreIds.Add(vectorStoreId);
        
        var agentResponse = await client.CreateAgentAsync(
            model: aiModel,
            name: request.Name,
            instructions: request.Instructions,
            tools: new List<ToolDefinition> { new FileSearchToolDefinition() },
            toolResources: new ToolResources() { FileSearch = fileSearchToolResource });
        
        return new Agent(
            agentResponse.Value.Id,
            agentResponse.Value.Name,
            agentResponse.Value.Instructions);
    }

    public async Task<Thread> CreateThreadAsync()
    {
        var client = CreateAgentsClient();

        var threadResponse = await client.CreateThreadAsync();

        return new Thread(threadResponse.Value.Id);
    }

    public async Task<Message> CreateRunAsync(CreateRunRequest request)
    {
        var client = CreateAgentsClient();

        await client.CreateMessageAsync(
            request.ThreadId,
            MessageRole.User,
            request.Message);

        Response<ThreadRun> runResponse = await client.CreateRunAsync(
            request.ThreadId,
            request.AgentId,
            additionalInstructions: "");

        do
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            runResponse = await client.GetRunAsync(request.ThreadId, runResponse.Value.Id);
        } while (runResponse.Value.Status == RunStatus.Queued || runResponse.Value.Status == RunStatus.InProgress);

        if (runResponse.Value.Status == RunStatus.Failed)
        {
            return new Message(
                Guid.NewGuid().ToString(), 
                MessageRole.User.ToString(),
                $"Error: {runResponse.Value.LastError.Message}");
        }
        
        Response<PageableList<ThreadMessage>> afterRunMessagesResponse =
            await client.GetMessagesAsync(request.ThreadId, order: ListSortOrder.Descending, limit: 1);

        var message = afterRunMessagesResponse.Value.Data.FirstOrDefault();

        if (message is null)
        {
            throw new Exception("No messages found after run.");
        }

        StringBuilder text = new();

        foreach (var contentItem in message.ContentItems)
        {
            if (contentItem is MessageTextContent textItem)
            {
                var annotations = textItem.Annotations;

                if (annotations.Any())
                {
                    var formattedText = textItem.Text;
                    
                    foreach (var annotation in annotations)
                    {
                        if (annotation is MessageTextFileCitationAnnotation messageTextFileCitationAnnotation)
                        {
                            formattedText = formattedText.Replace(messageTextFileCitationAnnotation.Text, $" ({_annotationMark} {messageTextFileCitationAnnotation.FileId})");
                        }
                    }
                    text.AppendLine(formattedText);
                }
                else
                {
                    text.AppendLine(textItem.Text);
                }
            }
        }

        if (message.ContentItems.Count == 1 && text.Length > 0)
        {
            text.Length--;
        }

        return new Message(message.Id, message.Role.ToString(), text.ToString());
    }

    public async Task<IEnumerable<Message>> ListMessagesAsync(string threadId)
    {
        var client = CreateAgentsClient();

        Response<PageableList<ThreadMessage>> messagesResponse = await client.GetMessagesAsync(threadId);

        return messagesResponse.Value.Data.Select(message =>
        {
            StringBuilder text = new();

            foreach (var contentItem in message.ContentItems)
            {
                if (contentItem is MessageTextContent textItem)
                {
                    text.AppendLine(textItem.Text);
                }
            }

            if (message.ContentItems.Count == 1 && text.Length > 0)
            {
                text.Length--;
            }

            return new Message(message.Id, message.Role.ToString(), text.ToString());
        });
    }

    private async Task<string> UploadFileAsync(string content, string fileName)
    {
        var client = CreateAgentsClient();
        
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var fileResponse = await client.UploadFileAsync(stream, AgentFilePurpose.Agents, fileName);

        return fileResponse.Value.Id;
    }
    
    private async Task AddFilesToVectorStoreAsync(string vectorStoreId, IList<string> fileIds)
    {
        var client = CreateAgentsClient();

        foreach (var fileId in fileIds)
        {
            await client.CreateVectorStoreFileAsync(vectorStoreId, fileId);
        }
    }
    
    private async Task<string> CreateDocVectorStoreAsync(IList<string> files)
    {
        var client = CreateAgentsClient();

        var vectorStore = await client.CreateVectorStoreAsync(fileIds: files, name: "my-docs");

        await AddFilesToVectorStoreAsync(vectorStore.Value.Id, files);
        
        return vectorStore.Value.Id;
    }
}