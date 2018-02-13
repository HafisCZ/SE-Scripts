/*
 * Script constants
*/
public readonly int[] ICE_TO_H2_CONV_RATIO = { 9, 4 };
public readonly int[] ICE_TO_H2_CONV_SPEED = { 501, 166 };
public readonly int[] HTHRUSTER_CONS_SPEED = { 1092, 6426, 109, 514 };

public readonly string TEXT_PANEL_ID = "#H_STATUS";

/*
 * Variables
*/
private List<IMyTextPanel> panels = new List<IMyTextPanel>();

private List<IMyGasTank> tanks = new List<IMyGasTank>();

private List<IMyTerminalBlock> generators = new List<IMyTerminalBlock>();

private List<IMyInventory> generatorsInventories = new List<IMyInventory>();
private List<IMyInventory> cargoInventories = new List<IMyInventory>();

private int consumptionMaxRate = 0;
private int conversionRate = 0;
private int storedAmount = 0;
private int consumptionRate = 0;
private int potentialAmount = 0;

// Large = 0, Small = 1
private int gridSize = 0;

/*
 * Constructor
*/
public Program()
{
    // Size of grid
    List<IMyProgrammableBlock> programmableBlocks = new List<IMyProgrammableBlock>();
    GridTerminalSystem.GetBlocksOfType<IMyProgrammableBlock>(programmableBlocks);

    gridSize = (int)programmableBlocks[0].CubeGrid.GridSizeEnum;

    Echo("Grid Size: " + (gridSize == 1 ? "Small" : "Large"));

    // Output panels
    List<IMyTerminalBlock> foundBlocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.SearchBlocksOfName(TEXT_PANEL_ID, foundBlocks);
    panels = FilterBlockType<IMyTextPanel, IMyTerminalBlock>(foundBlocks);

    // Maximum consumption rate
    List<IMyThrust> thrusters = new List<IMyThrust>();
    GridTerminalSystem.GetBlocksOfType<IMyThrust>(thrusters);
    for (int i = 0; i < thrusters.Count; i++)
    {
        string thrusterBlockName = thrusters[i].BlockDefinition.SubtypeId;
        if (thrusterBlockName.Contains("Hydro"))
        {
            consumptionMaxRate -= HTHRUSTER_CONS_SPEED[2 * gridSize + (thrusterBlockName.Contains("Small") ? 0 : 1)];
        }
    }

    // Cargo
    List<IMyTerminalBlock> cargo = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(cargo);

    cargoInventories = GetInventories(cargo);

    // Gas generators
    GridTerminalSystem.GetBlocksOfType<IMyGasGenerator>(generators);
    conversionRate = ICE_TO_H2_CONV_SPEED[gridSize] * generators.Count;

    generatorsInventories = GetInventories(generators);

    // Hydrogen tanks
    GridTerminalSystem.GetBlocksOfType<IMyGasTank>(tanks);
}

/*
 * Main
*/
public void Main(string argument)
{
    // Save current stored amount
    int lastStoredAmount = storedAmount;
    storedAmount = 0;

    // Amount in tanks
    for (int i = 0; i < tanks.Count; i++)
    {
        storedAmount += (int)(tanks[i].Capacity * tanks[i].FilledRatio);
    }

    // Change in amount
    consumptionRate = storedAmount - lastStoredAmount;

    // Potential amount
    potentialAmount = ICE_TO_H2_CONV_RATIO[gridSize] * (GetItemCount(cargoInventories, "Ice") + GetItemCount(generatorsInventories, "Ice"));

    // Display data on panel
    StringBuilder outputString = new StringBuilder();
    outputString.Append("H2 status: " + storedAmount + " + \t" + potentialAmount + "\n"
        + "Change /T: " + consumptionRate + " \t(" + consumptionMaxRate + ") Hps\n");

    int minimumBurnTime = (storedAmount + potentialAmount) / -consumptionMaxRate;

    if (consumptionRate < 0)
    {
        int potentialBurntime = (storedAmount + potentialAmount) / -consumptionRate;
        outputString.Append("Burn time: " + potentialBurntime + " \t(" + minimumBurnTime + ")s\n");

        if (consumptionRate < -conversionRate && generators.Count > 0)
        {
            outputString.Append("Not enough hydrogen generation.\nEngines may fail after ~" + (-storedAmount / consumptionRate) + " seconds!");
        }
    } else
    {
        outputString.Append("Burn time: ~ \t(" + minimumBurnTime + ")s\n");
    }

    for (int i = 0; i < panels.Count; i++)
    {
        panels[i].WritePublicText(outputString);
    }
}

/*
 * Returns count of items specified by word filter
*/
public int GetItemCount(List<IMyInventory> inventories, string filter)
{
    int count = 0;
    for (int i = 0; i < inventories.Count; i++)
    {
        List<IMyInventoryItem> inventoryItems = inventories[i].GetItems();
        for (int j = 0; j < inventoryItems.Count; j++)
        {
            if (inventoryItems[j].Content.SubtypeName.Equals(filter))
            {
                count += inventoryItems[j].Amount.ToIntSafe();
            }
        }
    }

    return count;
}

/*
 * Returns all inventories of supplied IMyTerminalBlocks, trashes invalid or missing inventories
*/
public List<IMyInventory> GetInventories(List<IMyTerminalBlock> blocks)
{
    List<IMyInventory> inventories = new List<IMyInventory>();
    for (int i = 0; i < blocks.Count; i++)
    {
        for (int j = 0; j < blocks[i].InventoryCount; j++)
        {
            inventories.Add(blocks[i].GetInventory(j));
        }
    }

    return inventories;
}

/*
 * Returns blocks only of supplied type
*/
public List<T> FilterBlockType<T, U>(IList<U> blocks) where T : U where U : IMyCubeBlock
{
    List<T> filteredBlocks = new List<T>();
    for (int i = 0; i < blocks.Count; i++)
    {
        if (blocks[i] is T)
        {
            filteredBlocks.Add((T) blocks[i]);
        }
    }

    return filteredBlocks;
}
