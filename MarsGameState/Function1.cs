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
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace MarsGameState
{
    public static class Function1
    {
        private static readonly string GAME_STATES_TABLE_NAME = "GameStates";
        private static readonly string PLAYER_ROLES_TABLE_NAME = "PlayerRoles";

        private static readonly string[] files = new string[] { "code.js", "style.css", "favicon.ico" };
        private static readonly string[] chapterHtml = new string[] { "GameStateCreateJoin.html", "GameStateIntroduction.html", "GameStateChapter1.html" };

        [FunctionName("Function1")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "{game_id?}/{action1?}")] HttpRequest req,
            string game_id,
            string action1,
            ILogger log, ExecutionContext context)
        {
            try
            {
                if (req.Method == "GET")
                {
                    string action = req.Query["action"];
                    if (action == "GET_POSITION")
                    {
                        string playerName = req.Cookies["PlayerName"];

                        var gameState = await LoadGameStateAsync(game_id, log, context); // [Chapter, PositionInChapter, PlayerName, ActivePlayer]
                        JObject jsonObj = JObject.FromObject(gameState);
                        jsonObj.Add("PlayerName", playerName);

                        var playerRoles = await LoadPlayerRolesAsync(game_id, log, context); // [PlayerA, PLayerB, PlayerC]
                        jsonObj.Add("RolesDistribution", JArray.FromObject(playerRoles));

                        string content1 = JsonConvert.SerializeObject(jsonObj);
                        var cr1 = new ContentResult()
                        {
                            Content = content1,
                            ContentType = "application/json",
                        };

                        return cr1;
                    }
                    else
                    {
                        string fileName = "";
                        string fileMimeType = "";

                        if (files.Contains(game_id))
                        {
                            fileName = game_id;
                            fileMimeType = "text/css";
                        }
                        else { 
                            var gameState = await LoadGameStateAsync(game_id, log, context);
                            
                            fileName = chapterHtml[gameState?.GameChapter ?? 0];
                            fileMimeType = "text/html";
                        }

                        string filePathName = Path.Combine(context.FunctionAppDirectory, "Content\\" + fileName);
                        var content1 = File.ReadAllText(filePathName);

                        var cr = new ContentResult()
                        {
                            Content = content1,
                            ContentType = fileMimeType,
                        };

                        return cr;
                    }
                }
                else if (req.Method == "POST")
                {
                    GameState gameState = null;
                    bool logon = false;
                    string playerName = req.Cookies["PlayerName"];
                    if (game_id == null || game_id == "")
                    {
                        logon = true; // triggers the redirect

                        playerName = req.Form["player_name"];
                        SetCookie(req, "PlayerName", playerName, DateTimeOffset.Now.AddDays(1));

                        if (req.Form.ContainsKey("game_id") && req.Form["game_id"] != "")
                            game_id = req.Form["game_id"]; // joined the game
                        else
                            game_id = GenerateId(); // game creation
                    }

                    gameState = (await LoadGameStateAsync(game_id, log, context)) ?? new GameState(game_id, playerName);
                    if (req.Form.ContainsKey("chapter"))
                        gameState.GameChapter = Int32.Parse(req.Form["chapter"]);

                    if (req.Form.ContainsKey("end_turn"))
                    {
                        var playerRoles = await LoadPlayerRolesAsync(game_id, log, context); // [PlayerA, PLayerB, PlayerC]
                        gameState.ActivePlayer = (gameState.ActivePlayer + 1) % playerRoles.Count();
                    }

                    if (gameState.GameChapter == 1)
                    { // player chosen a role
                        string role = "";

                        if (req.Form.ContainsKey("role"))
                            role = req.Form["role"];

                        var playerRole = new PlayerRole(game_id, playerName, role);
                        await SavePlayerRoleAsync(playerRole, log, context);
                    }
                    else if (gameState.GameChapter == 2)
                    { // progress in chapter changed OR chapter changed
                        if(req.Form.ContainsKey("position"))
                            gameState.Position =  Int32.Parse(req.Form["position"]);
                    }

                    await SaveGameStateAsync(gameState, log, context); // not always make sense - so far on game create and position update

                    // create or join
                    if (logon)
                    {
                        RedirectResult rr = new RedirectResult(req.Path + game_id);
                        return rr;
                    }
                    else
                        return new OkObjectResult("OK");
                }
                else throw new NotImplementedException();
            }
            catch (Exception e)
            {
                log.LogError(e.Message);
                return new OkObjectResult(e.Message);
            }
        }

        private static void SetCookie(HttpRequest req, string cookie_name, string cookie_value, DateTimeOffset time_to_expiration)
        {
            CookieOptions option = new CookieOptions();
            option.Expires = time_to_expiration;
            option.HttpOnly = true;
            req.HttpContext.Response.Cookies.Append(cookie_name, cookie_value, option);
        }

        private static async Task<string> SavePlayerRoleAsync(PlayerRole playerRole, ILogger log, ExecutionContext context)
        {
            CloudStorageAccount storageAccount1 = GetCloudStorageAccount(log, context);
            var tableClient1 = storageAccount1.CreateCloudTableClient();
            var table1 = tableClient1.GetTableReference(PLAYER_ROLES_TABLE_NAME);
            await table1.CreateIfNotExistsAsync();

            TableResult tr = await table1.ExecuteAsync(TableOperation.InsertOrReplace(playerRole));
            return playerRole.GameId;
        }

        private static async Task<PlayerRole> LoadPlayerRoleAsync(string gameId, string playerName, ILogger log, ExecutionContext context)
        {
            CloudStorageAccount storageAccount1 = GetCloudStorageAccount(log, context);
            var tableClient1 = storageAccount1.CreateCloudTableClient();
            var table1 = tableClient1.GetTableReference(PLAYER_ROLES_TABLE_NAME);

            string filterPK = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, gameId);
            string filterRK = TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, playerName);

            var query = new TableQuery<PlayerRole>().Where(TableQuery.CombineFilters(filterPK, TableOperators.And, filterRK));

            var tr = await table1.ExecuteQuerySegmentedAsync(query, null);
            return tr.SingleOrDefault();
        }

        private static async Task<IEnumerable<PlayerRole>> LoadPlayerRolesAsync(string gameId, ILogger log, ExecutionContext context)
        {
            CloudStorageAccount storageAccount1 = GetCloudStorageAccount(log, context);
            var tableClient1 = storageAccount1.CreateCloudTableClient();
            var table1 = tableClient1.GetTableReference(PLAYER_ROLES_TABLE_NAME);

            string filter = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, gameId);
            var query = new TableQuery<PlayerRole>().Where(filter);

            var tr = await table1.ExecuteQuerySegmentedAsync(query, null);
            return tr.Results;
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
