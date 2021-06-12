using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Text;

namespace MarsGameState
{
    internal class GameLogRecord : TableEntity
    {
        public string GameId { get; set; }
        public string PlayerName { get; set; }
        public string Message { get; set; }

        public GameLogRecord() { }
        public GameLogRecord(string _GameId, string _PlayerName, string _Message)
        {
            Message = _Message;
            PlayerName = _PlayerName;

            GameId = PartitionKey = _GameId;
            RowKey = ((DateTimeOffset)DateTime.Now).ToUnixTimeSeconds().ToString();
        }
    }

}
