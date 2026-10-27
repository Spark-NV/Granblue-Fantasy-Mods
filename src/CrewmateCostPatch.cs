using System.Buffers.Binary;

namespace Spark.Gbfr.Mods;

internal static class CrewmateCostPatch
{
    public const string TradeTablePath = "system/table/trade.tbl";
    public const string TierTablePath = "system/table/item_tier_map.tbl";
    public const string MaterialTablePath = "system/table/item_material_list.tbl";

    private const uint EmptyHash = 0x887AE0B0;
    private const int TradeRowSize = 76;
    private const int TradeMinQuestOffset = 0x00;
    private const int TradeItemOffset = 0x18;
    private const int TradeTierOffset = 0x1C;
    private const int TradeShopOffset = 0x20;
    private const uint TreasureTradeShop = 3;
    private const uint FirstQuest = 0x100000;
    private const int TradeStockCapOffset = 0x44;
    private const int TierRowSize = 60;
    private const int TierKeyOffset = 0x38;
    private const int TierMaterials = 14;
    private const int RecipeRowSize = 176;
    private const int RecipeItemSlots = 21;
    private const int RecipeCountsOffset = RecipeItemSlots * 4;
    private const int RecipeKeyOffset = 0xA8;

    public sealed record Counts(int Trades, int Recipes);

    public static Counts? Apply(byte[] trade, byte[] tiers, byte[] recipes, int cobblestoneCost)
    {
        if (Rows(trade, TradeRowSize) is not long tradeRows || Rows(tiers, TierRowSize) is not long tierRows
            || Rows(recipes, RecipeRowSize) is not long recipeRows)
            return null;

        var cards = Enumerable.Range(0, 8).Select(i => CaptainUnlockPatch.GameHash($"ITEM_23_{i:D4}")).ToHashSet();
        uint cobblestone = CaptainUnlockPatch.GameHash("ITEM_01_0000");

        var tierRecipes = new Dictionary<uint, List<uint>>();
        var recipeUse = new Dictionary<uint, int>();
        for (long r = 0; r < tierRows; r++)
        {
            var row = tiers.AsSpan(checked((int)(8 + r * TierRowSize)), TierRowSize);
            var list = new List<uint>();
            for (int i = 0; i < TierMaterials; i++)
            {
                uint material = BinaryPrimitives.ReadUInt32LittleEndian(row[(i * 4)..]);
                if (material != 0)
                    list.Add(material);
            }
            tierRecipes[BinaryPrimitives.ReadUInt32LittleEndian(row[TierKeyOffset..])] = list;
        }
        var tierUse = new Dictionary<uint, int>();
        for (long r = 0; r < tradeRows; r++)
        {
            uint tier = BinaryPrimitives.ReadUInt32LittleEndian(trade.AsSpan(checked((int)(8 + r * TradeRowSize + TradeTierOffset))));
            tierUse[tier] = tierUse.GetValueOrDefault(tier) + 1;
        }
        foreach (var (tier, list) in tierRecipes)
            foreach (var recipe in list)
                recipeUse[recipe] = recipeUse.GetValueOrDefault(recipe) + tierUse.GetValueOrDefault(tier);

        var recipeRow = new Dictionary<uint, int>();
        for (long r = 0; r < recipeRows; r++)
            recipeRow[BinaryPrimitives.ReadUInt32LittleEndian(recipes.AsSpan(checked((int)(8 + r * RecipeRowSize + RecipeKeyOffset))))] = (int)r;

        int trades = 0, changedRecipes = 0;
        for (long r = 0; r < tradeRows; r++)
        {
            var row = trade.AsSpan(checked((int)(8 + r * TradeRowSize)), TradeRowSize);
            if (!cards.Contains(BinaryPrimitives.ReadUInt32LittleEndian(row[TradeItemOffset..])))
                continue;
            uint tier = BinaryPrimitives.ReadUInt32LittleEndian(row[TradeTierOffset..]);
            if (!tierRecipes.TryGetValue(tier, out var list) || list.Count != 1
                || recipeUse.GetValueOrDefault(list[0]) != 1 || !recipeRow.TryGetValue(list[0], out int rr))
                continue;

            var recipe = recipes.AsSpan(8 + rr * RecipeRowSize, RecipeRowSize);
            bool singleItem = true;
            for (int i = 1; i < RecipeItemSlots; i++)
                singleItem &= BinaryPrimitives.ReadUInt32LittleEndian(recipe[(i * 4)..]) is EmptyHash or 0;
            if (!singleItem)
                continue;

            BinaryPrimitives.WriteUInt32LittleEndian(recipe, cobblestone);
            BinaryPrimitives.WriteInt32LittleEndian(recipe[RecipeCountsOffset..], cobblestoneCost);
            BinaryPrimitives.WriteUInt32LittleEndian(row[TradeStockCapOffset..], 0xFFFFFFFF);
            BinaryPrimitives.WriteUInt32LittleEndian(row[TradeMinQuestOffset..], FirstQuest);
            BinaryPrimitives.WriteUInt32LittleEndian(row[TradeShopOffset..], TreasureTradeShop);
            trades++;
            changedRecipes++;
        }
        return trades > 0 ? new Counts(trades, changedRecipes) : null;
    }

    private static long? Rows(byte[] table, int rowSize)
    {
        if (table.Length < 8)
            return null;
        long rows = BinaryPrimitives.ReadInt64LittleEndian(table);
        return rows > 0 && 8 + rows * rowSize == table.LongLength ? rows : null;
    }
}
