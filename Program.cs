
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Newtonsoft.Json.Linq;
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
        string parentWorkItemId = "1353003"; // Replace with the actual WR ID
        //await CopyWRToTask(parentWorkItemId);

        var missingWRs = await GetMissingTasksFromQuery();
        await CreateTasksForMissingWorkItems(missingWRs);
        //await CopyWRToTask("1324455");
    }

    static async Task CreateTasksForMissingWorkItems(List<int>? missingWRs)
    {
        foreach (var workItemId in missingWRs)
        {
            // Fetch additional information for the work item if needed (like title, description, areaPath, etc.)
           // var workItemDetails = await GetWorkItemDetails(workItemId);

            // Create a task for the current work item
            await CreatetasksFromWR(workItemId.ToString());
        }
    }
    static async Task<List<int>> GetMissingTasksFromQuery()
    {
        string collectionUrl = "https://dev.azure.com/uhaul/"; 
        string projectName = "U-Haul IT";

        //var queryUrl = "https://dev.azure.com/uhaul/U-Haul%20IT/_apis/wit/wiql/068b5dbc-6545-4bf5-82b7-9c3e6522a643?api-version=7.1-preview.3";  // The URL to the query API
        //var client = new HttpClient();
        //client.DefaultRequestHeaders.Add("Authorization", "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + pat))); 

        //HttpResponseMessage response = await client.GetAsync(queryUrl);
        //response.EnsureSuccessStatusCode();

        //var responseContent = await response.Content.ReadAsStringAsync();

        ////using (JsonDocument doc = JsonDocument.Parse(responseContent))
        ////{
        ////    return doc.RootElement;
        ////}
        //var jsonResponse = JObject.Parse(responseContent);
        //var workItems = jsonResponse["workItems"].Select(wi => (int)wi["id"]).ToList();  // Extract work item ids

        //return workItems;

        VssConnection connection = new VssConnection(new Uri(orgUrl), new VssBasicCredential(string.Empty, pat));
        var workItemTrackingHttpClient = connection.GetClient<WorkItemTrackingHttpClient>();

        string wiqlQuery = $@"
            SELECT
                [System.Id]
            FROM workitemLinks
            WHERE
                (
                    [Source].[System.TeamProject] = '{project}'
                    AND [Source].[System.IterationPath] = @currentIteration('[U-Haul IT]\uhaul.com Development <id:bf28a9bf-fe86-431b-997b-b3ccd170156a>')
                    AND [Source].[System.WorkItemType] IN ('Work Request', 'Bug')
                    AND (
                        [Source].[System.AreaPath] UNDER 'U-Haul IT\Chris Jestice\Neal Valiant'
                        OR [Source].[System.AreaPath] UNDER 'U-Haul IT\Chris Jestice\Uhaul.com'
                    )
                    AND [Source].[System.State] <> 'Closed'
                    AND NOT [Source].[System.Title] CONTAINS 'Regression'
                )
                AND ([System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward')
                AND ([Target].[System.TeamProject] = '{project}'
                    AND [Target].[System.WorkItemType] = 'Task')
                MODE (DoesNotContain)";

        // Execute the query
        var wiql = new Wiql() { Query = wiqlQuery };
        var workItemLinks = await workItemTrackingHttpClient.QueryByWiqlAsync(wiql);

        // Extract the work item IDs
        var workItemIds = workItemLinks.WorkItems.Select(wi => wi.Id).ToList();
        return workItemIds;

    }

    static async Task CreatetasksFromWR(string parentId, bool onlyYourTasks=true)
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
            string assignedTo = doc.RootElement.GetProperty("fields").GetProperty("System.AssignedTo").GetProperty("displayName").GetString();
            string effort = doc.RootElement.GetProperty("fields").GetProperty("Microsoft.VSTS.Scheduling.Effort").GetDouble().ToString();

            if(assignedTo?.ToUpper() != "VIKAS BRUNGI")
            {
                return;
            }

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
    {{ ""op"": ""add"", ""path"": ""/fields/Microsoft.VSTS.Common.ItemDescription"", ""value"": ""{EscapeJsonString(description)}"" }},
    {{ ""op"": ""add"", ""path"": ""/fields/System.AreaPath"", ""value"": ""{EscapeJsonString(areaPath)}"" }},
    {{ ""op"": ""add"", ""path"": ""/fields/System.IterationPath"", ""value"": ""{EscapeJsonString(iterationPath)}"" }},
    {{ ""op"": ""add"", ""path"": ""/fields/System.AssignedTo"", ""value"": ""{assignedTo}"" }},
    {{ ""op"": ""add"", ""path"": ""/fields/Microsoft.VSTS.Scheduling.Effort"", ""value"": {effort} }},
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