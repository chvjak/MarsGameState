using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Text;

namespace MarsGameState
{
    internal class PlayerRole : TableEntity
    {
        public string Role { get; set; }
        public string PlayerName { get; set; }
        public string GameId { get; set; }

        public PlayerRole() { }
        public PlayerRole(string _GameId, string _PlayerName, string _Role)
        {
            Role = _Role;
            GameId = PartitionKey = _GameId;
            PlayerName = RowKey = _PlayerName;
        }
    }

}
