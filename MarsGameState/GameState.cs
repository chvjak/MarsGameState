using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Text;

namespace MarsGameState
{
    internal class GameState : TableEntity
    {
        public string Id { get; set; } 
        public string DateCreate { get; set; }
        public int Position { get; set; }
        public int MaxPosition { get; set; }

        public GameState() { }
        public GameState(string _id, int _max_position)
        {
            Id = _id;
            DateCreate = DateTime.Now.ToString();
            Position = 0;
            MaxPosition = _max_position;

            PartitionKey = "0"; // use single partition 
            RowKey = _id;
        }
    }

}
