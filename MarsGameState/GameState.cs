using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Text;

namespace MarsGameState
{
    internal class GameState : TableEntity
    {
        public string Id { get; set; } 
        public string DateCreate { get; set; } // can be used to purge old stale games
        public string GameHostName { get; set; } // The one who can advance the chapters
        public int Position { get; set; }
        public int GameChapter { get; set; } // lobby - chapter 0, chapter 1 , chapter 2, ..
        public bool NextChapterReady { get; set; } // Chapter 0 - every role distributrd, Chapter 1 - Position 20, ...

        public GameState() { }
        public GameState(string _Id, string _GameHostName)
        {
            Id = _Id;
            DateCreate = DateTime.Now.ToString();
            Position = 0;
            GameChapter = 1;
            GameHostName = _GameHostName;

            PartitionKey = _GameHostName;
            RowKey = _Id;
        }
    }

}
