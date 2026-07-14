using System;
using System.IO;
using UnityEngine;

namespace Infrastructure.LevelStorage.Extensions.Data
{
    public class Vector2IntData
    {
        public Vector2Int Position;

        private const int _convertValue = 100;
        
        public Vector2IntData(BinaryReader reader)
        {
            Position = new Vector2Int(reader.ReadInt16(), reader.ReadInt16());
            Position /= _convertValue;
        }
        
        public Vector2IntData(Vector2Int position)
        {
            Position = position;
        }

        public void SaveData(BinaryWriter writer)
        {
            writer.Write((Int16)Mathf.RoundToInt(Position.x * _convertValue));
            writer.Write((Int16)Mathf.RoundToInt(Position.y * _convertValue));
        }
    }
}
