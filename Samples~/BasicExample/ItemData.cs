using System.Collections.Generic;
using CsvXlsxToSo;
using UnityEngine;

namespace CsvXlsxToSo.Samples
{
    public enum ItemRarity
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary
    }

    [CreateAssetMenu(fileName = "ItemData", menuName = "Samples/Item Data")]
    public class ItemData : ScriptableObject
    {
        [SpreadsheetId]
        public string id;

        [SpreadsheetColumn("Display Name")]
        public string displayName;

        public string description;
        public int goldValue;
        public float weight;
        public bool stackable;
        public ItemRarity rarity;
        public Color tintColor;
        public List<string> tags = new List<string>();
    }
}
