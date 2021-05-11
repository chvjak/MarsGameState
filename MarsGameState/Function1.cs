using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.WindowsAzure.Storage;
using Microsoft.Extensions.Configuration;
using System.Linq;

namespace MarsGameState
{
    public static class Function1
    {
        private static readonly string GAME_STATES_TABLE_NAME = "GameStates";
        //private static readonly string URL = "http://localhost:7071/";
        private static readonly string URL = "https://mars-mvp.azurewebsites.net/";

        [FunctionName("Function1")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "{game_id?}")] HttpRequest req,
            string game_id,
            ILogger log, ExecutionContext context)
        {
            try
            {
                if (game_id == null || game_id == "")
                { 
                    game_id = GenerateId();
                    RedirectResult rr = new RedirectResult(URL + game_id);
                    return rr;
                }

                if (req.Method == "GET")
                {
                    string action = req.Query["action"];
                    if (action == "GET_POSITION")
                    {
                        var gameState = await LoadGameStateAsync(game_id, log, context);

                        string content1 = JsonConvert.SerializeObject(gameState).ToString(); ;
                        var cr1 = new ContentResult()
                        {
                            Content = content1,
                            ContentType = "application/json",
                        };

                        return cr1;
                    }
                    else
                    {
                        // get the state, preferably without reloading, OR the page could have autorefresh
                        string htmlFilePath = Path.Combine(context.FunctionAppDirectory, "GameState.html");
                        var content1 = File.ReadAllText(htmlFilePath);

                        var cr = new ContentResult()
                        {
                            Content = content1,
                            ContentType = "text/html",
                        };

                        return cr;
                    }
                }
                else if (req.Method == "POST")
                {
                    var gameState = new GameState(game_id, 20);
                    gameState.Position = Int32.Parse(req.Form["position"]);
                    await SaveGameStateAsync(gameState, log, context);

                    RedirectResult rr = new RedirectResult(URL + game_id);
                    return rr;
                }
                else throw new NotImplementedException();
            }
            catch (Exception e)
            {
                log.LogError(e.Message);
                return new OkObjectResult(e.Message);
            }
        }

        private static async Task<string> SaveGameStateAsync(GameState gameState, ILogger log, ExecutionContext context)
        {
            log.LogInformation($"C# Http trigger function executed at: {DateTime.Now}");

            CloudStorageAccount storageAccount1 = GetCloudStorageAccount(log, context);
            var tableClient1 = storageAccount1.CreateCloudTableClient();
            var table1 = tableClient1.GetTableReference(GAME_STATES_TABLE_NAME);
            await table1.CreateIfNotExistsAsync();

            TableResult tr = await table1.ExecuteAsync(TableOperation.InsertOrReplace(gameState));

            log.LogInformation($"Entity {gameState.Id} is saved to table {GAME_STATES_TABLE_NAME}");
            return gameState.Id;
        }


        private static async Task<GameState> LoadGameStateAsync(string gameId, ILogger log, ExecutionContext context)
        {
            CloudStorageAccount storageAccount1 = GetCloudStorageAccount(log, context);
            var tableClient1 = storageAccount1.CreateCloudTableClient();
            var table1 = tableClient1.GetTableReference(GAME_STATES_TABLE_NAME);

            string filter = TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, gameId);
            TableQuery<GameState> query = new TableQuery<GameState>().Where(filter);

            var tr = await table1.ExecuteQuerySegmentedAsync(query, null);
            return tr.SingleOrDefault();
        }

        private static CloudStorageAccount GetCloudStorageAccount(ILogger logger, ExecutionContext executionContext)
        {
            var config = new ConfigurationBuilder()
                            .SetBasePath(executionContext.FunctionAppDirectory)
                            .AddJsonFile("local.settings.json", true, true)
                            .AddEnvironmentVariables().Build();
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(config["CloudStorageAccount"]);
            return storageAccount;
        }

        private static string GenerateId()
        {
            Random rnd = new Random();
            string result = "";
            string values = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
            for (int i = 0; i < 7; i++)
            {
                result += values[rnd.Next(0, 62)];
            }
            return result;
        }

    }
}
