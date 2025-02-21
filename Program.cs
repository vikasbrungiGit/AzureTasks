using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    private static readonly string orgUrl = "https://dev.azure.com/uhaul";
    private static readonly string project = "U-Haul IT";
    private static readonly string pat = "azure-key";

    static async Task Main(string[] args)
    {
        string parentWorkItemId = "1287264"; // Replace with the actual WR ID
        //await CopyWRToTask(parentWorkItemId);

        var missingWRs = await GetMissingTasksFromQuery();
        await CreateTasksForMissingWorkItems(missingWRs);
    }

    static async Task CreateTasksForMissingWorkItems(List<int>? missingWRs)
    {
        foreach (var workItemId in missingWRs)
        {
            // Fetch additional information for the work item if needed (like title, description, areaPath, etc.)
           // var workItemDetails = await GetWorkItemDetails(workItemId);

            // Create a task for the current work item
            await CopyWRToTask(workItemId.ToString());
        }
    }
    static async Task<List<int>> GetMissingTasksFromQuery()
    {
        var queryUrl = "https://dev.azure.com/uhaul/U-Haul%20IT/_apis/wit/queries/068b5dbc-6545-4bf5-82b7-9c3e6522a643?api-version=6.0";  // The URL to the query API
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + pat))); 

        HttpResponseMessage response = await client.GetAsync(queryUrl);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();

        //using (JsonDocument doc = JsonDocument.Parse(responseContent))
        //{
        //    return doc.RootElement;
        //}
        var jsonResponse = JObject.Parse(responseContent);
        var workItems = jsonResponse["workItems"].Select(wi => (int)wi["id"]).ToList();  // Extract work item ids

        return workItems;
    }

    static async Task CopyWRToTask(string parentId)
    {
        using (HttpClient client = new HttpClient())
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}")));

            // Step 1: Fetch WR details (Title & Description)
            string wrUri = $"{orgUrl}/{project}/_apis/wit/workitems/{parentId}?api-version=7.0";
            HttpResponseMessage wrResponse = await client.GetAsync(wrUri);
            if (!wrResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to fetch WR: {wrResponse.StatusCode}");
                return;
            }

            string wrResponseBody = await wrResponse.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(wrResponseBody);
            string title = doc.RootElement.GetProperty("fields").GetProperty("System.Title").GetString();
            string description = doc.RootElement.GetProperty("fields").GetProperty("System.Description").GetString();

            // Extract AreaPath and IterationPath
            string areaPath = doc.RootElement.GetProperty("fields").GetProperty("System.AreaPath").GetString();
            string iterationPath = doc.RootElement.GetProperty("fields").GetProperty("System.IterationPath").GetString();


            Console.WriteLine($"WR Title: {title}");
            Console.WriteLine($"WR Description: {description}");

            // Step 2: Create Task with copied Title & Description
            string taskUri = $"{orgUrl}/{project}/_apis/wit/workitems/$Task?api-version=7.0";

            // Correctly formatted JSON patch document
            string jsonBody = $@"
[
    {{ ""op"": ""add"", ""path"": ""/fields/System.Title"", ""value"": ""{title}"" }},
    {{ ""op"": ""add"", ""path"": ""/fields/System.Description"", ""value"": ""{EscapeJsonString(description)}"" }},
    {{ ""op"": ""add"", ""path"": ""/fields/Microsoft.VSTS.Common.ItemDescription"", ""value"": ""Copied from WR #{parentId}"" }},
    {{ ""op"": ""add"", ""path"": ""/fields/System.AreaPath"", ""value"": ""{EscapeJsonString(areaPath)}"" }},
    {{ ""op"": ""add"", ""path"": ""/fields/System.IterationPath"", ""value"": ""{EscapeJsonString(iterationPath)}"" }},
    {{ ""op"": ""add"", ""path"": ""/relations/-"", ""value"": 
                    {{ ""rel"": ""System.LinkTypes.Hierarchy-Reverse"", 
                       ""url"": ""{orgUrl}/{project}/_apis/wit/workItems/{parentId}"" }} 
                }}
]";

            //string working1jsonBody = $@"
            //[
            //    {{ ""op"": ""add"", ""path"": ""/fields/System.Title"", ""value"": ""{title}"" }},
            //    {{ ""op"": ""add"", ""path"": ""/fields/System.Description"", ""value"": ""{description}"" }},
            //    {{ ""op"": ""add"", ""path"": ""/fields/Microsoft.VSTS.Common.ItemDescription"", ""value"": ""{itemDescription}"" }},
            //    {{ ""op"": ""add"", ""path"": ""/relations/-"", ""value"": 
            //        {{ ""rel"": ""System.LinkTypes.Hierarchy-Reverse"", 
            //           ""url"": ""{orgUrl}/{project}/_apis/wit/workItems/{parentId}"" }} 
            //    }}
            //]";

            //string jsonBody2 = $@"
            //[
            //    {{ ""op"": ""add"", ""path"": ""/fields/System.Title"", ""value"": ""{title}"" }},
            //    {{ ""op"": ""add"", ""path"": ""/fields/System.Description"", ""value"": ""{description}"" }},
            //    {{ ""op"": ""add"", ""path"": ""/fields/Microsoft.VSTS.Common.ItemDescription"", ""value"": ""{itemDescription}"" }},
            //    {{ ""op"": ""add"", ""path"": ""/relations/-"", ""value"": 
            //        {{ ""rel"": ""System.LinkTypes.Hierarchy-Reverse"", 
            //           ""url"": ""{orgUrl}/{project}/_apis/wit/workItems/{parentId}"" }} 
            //    }}
            //]";

            // Create content with proper headers
            HttpContent content = new StringContent(jsonBody, Encoding.UTF8, "application/json-patch+json");
            HttpResponseMessage response = await client.PostAsync(taskUri, content);

            string responseBody = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
                Console.WriteLine($"Task Created Successfully! Response: {responseBody}");
            else
                Console.WriteLine($"Error: {response.StatusCode} - {responseBody}");
        }

        static string EscapeJsonString(string input)
        {
            return input.Replace("\\", "\\\\")
                        .Replace("\"", "\\\"")
                        .Replace("\r", "\\r")
                        .Replace("\n", "\\n")
                        .Replace("\t", "\\t");
        }
    }
}