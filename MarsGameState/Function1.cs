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
        private static readonly string[] files = new string[] { "code.js", "style.css", "favicon.ico" };
        private static readonly string[] chapterHtml = new string[] { "GameStateCreateJoin.html", "GameStateIntroduction.html", "GameStateChapter1.html" };

        private static readonly Dictionary<string, string> mimeTypeMap = new Dictionary<string, string> { { "ico" , "image/x-icon" }, { "html", "text/html" }, { "js" , "text/javascript" }, { "css" , "text/css" } };

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
                        log.LogError("Loading game state :" + game_id, null);

                        string playerName = req.Cookies["PlayerName"];

                        var gameState = await LoadGameStateAsync(game_id, log, context); // [Chapter, PositionInChapter, PlayerName, ActivePlayer]
                        JObject jsonObj = JObject.FromObject(gameState);
                        jsonObj.Add("PlayerName", playerName);

                        var playerRoles = await LoadMultipleAsync<PlayerRole>(game_id, log, context); // [PlayerA, PLayerB, PlayerC]
                        jsonObj.Add("RolesDistribution", JArray.FromObject(playerRoles));

                        var gameLogRecords = await LoadMultipleAsync<GameLogRecord>(game_id, log, context);
                        jsonObj.Add("GameLogRecords", JArray.FromObject(gameLogRecords
                            .OrderByDescending(x => x.RowKey)
                            .Take(20)
                            .Select(x=> new Dictionary<string, string>{
                                { "Timestamp", x.Timestamp.DateTime.ToShortDateString() + " " + x.Timestamp.DateTime.ToLongTimeString() },
                                { "Message", x.Message}
                            })
                            ));

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
                            fileMimeType = mimeTypeMap[fileName.Split(".")[1]];
                        }
                        else {
                            var gameState = await LoadGameStateAsync(game_id, log, context);

                            fileName = chapterHtml[gameState?.GameChapter ?? 0];
                            fileMimeType = mimeTypeMap[fileName.Split(".")[1]];
                        }

                        log.LogError("Loading a file :" + fileName, null);

                        string filePathName = Path.Combine(context.FunctionAppDirectory, "Content\\" + fileName);
                        using (FileStream fs = new FileStream(filePathName, FileMode.Open))
                        {
                            var byteArray = new Byte[fs.Length];
                            fs.Read(byteArray, 0, (int)fs.Length);
                            return new FileContentResult(byteArray, fileMimeType);
                        }
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
                        {
                            game_id = req.Form["game_id"]; // joined the game

                            await WriteGameLogMessage(game_id, log, context, playerName, $"{playerName} joined the game");
                        }
                        else
                        {
                            game_id = GenerateId(); // game creation

                            await WriteGameLogMessage(game_id, log, context, playerName, $"{playerName} created the game");
                        }
                    }

                    gameState = (await LoadGameStateAsync(game_id, log, context)) ?? new GameState(game_id, playerName);
                    if (req.Form.ContainsKey("chapter"))
                    { 
                        gameState.GameChapter = Int32.Parse(req.Form["chapter"]);

                        await WriteGameLogMessage(game_id, log, context, playerName, $"{playerName} set chapter to {gameState.GameChapter}");
                    }

                    if (req.Form.ContainsKey("end_turn"))
                    {
                        var playerRoles = await LoadMultipleAsync<PlayerRole>(game_id, log, context); // [PlayerA, PLayerB, PlayerC]
                        gameState.ActivePlayer = (gameState.ActivePlayer + 1) % playerRoles.Count();

                        await WriteGameLogMessage(game_id, log, context, playerName, $"{playerName} ended the turn");
                    }

                    if (gameState.GameChapter == 1)
                    { // player chosen a role
                        string role = "";

                        if (req.Form.ContainsKey("role"))
                            role = req.Form["role"];

                        var playerRole = new PlayerRole(game_id, playerName, role);
                        await SaveSingleAsync<PlayerRole>(playerRole, log, context);

                        await WriteGameLogMessage(game_id, log, context, playerName, $"{playerName} changed the role to {role}");
                    }
                    else if (gameState.GameChapter == 2)
                    { // progress in chapter changed OR chapter changed
                        if (req.Form.ContainsKey("position"))
                        {
                            gameState.Position = Int32.Parse(req.Form["position"]);

                            await WriteGameLogMessage(game_id, log, context, playerName, $"{playerName} set chapter position to {gameState.Position}");
                        }
                    }

                    await SaveSingleAsync<GameState>(gameState, log, context); // not always make sense - so far on game create and position update

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
                log.LogError(e.ToString());
                return new OkObjectResult(e.Message);
            }
        }

        private static async Task WriteGameLogMessage(string game_id, ILogger log, ExecutionContext context, string playerName, string message)
        {
            var gameLogRecord = new GameLogRecord(game_id, playerName, message);
            await SaveSingleAsync<GameLogRecord>(gameLogRecord, log, context);
        }

        private static void SetCookie(HttpRequest req, string cookie_name, string cookie_value, DateTimeOffset time_to_expiration)
        {
            var option = new CookieOptions
            {
                Expires = time_to_expiration,
                HttpOnly = true
            };
            req.HttpContext.Response.Cookies.Append(cookie_name, cookie_value, option);
        }

        private static string Pluralize(string Name)
        {
            return Name + "s";
        }

        private static async Task<string> SaveSingleAsync<T>(T entity, ILogger log, ExecutionContext context) where T : ITableEntity, new()
        {
            CloudStorageAccount storageAccount1 = GetCloudStorageAccount(log, context);
            var tableClient1 = storageAccount1.CreateCloudTableClient();
            var table1 = tableClient1.GetTableReference(Pluralize(typeof(T).Name));
            await table1.CreateIfNotExistsAsync();

            TableResult tr = await table1.ExecuteAsync(TableOperation.InsertOrReplace(entity));
            return entity.PartitionKey;
        }

        private static async Task<IEnumerable<T>> LoadMultipleAsync<T>(string gameId, ILogger log, ExecutionContext context) where T : ITableEntity, new()
        {
            CloudStorageAccount storageAccount1 = GetCloudStorageAccount(log, context);
            var tableClient1 = storageAccount1.CreateCloudTableClient();
            var table1 = tableClient1.GetTableReference(Pluralize(typeof(T).Name));

            string filter = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, gameId);
            var query = new TableQuery<T>().Where(filter);
            var tr = await table1.ExecuteQuerySegmentedAsync(query, null);
            return tr.Results;
        }

        private static async Task<GameState> LoadGameStateAsync(string gameId, ILogger log, ExecutionContext context)
        {
            CloudStorageAccount storageAccount1 = GetCloudStorageAccount(log, context);
            var tableClient1 = storageAccount1.CreateCloudTableClient();
            var table1 = tableClient1.GetTableReference(Pluralize(typeof(GameState).Name));

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
