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
IMyTextPanel outputPanel = null;

List<IMyTerminalBlock> cargo = new List<IMyTerminalBlock>();
List<IMyGasTank> tanks = new List<IMyGasTank>();
List<IMyTerminalBlock> generators = new List<IMyTerminalBlock>();

int consumptionMaxRate = 0;
int conversionRate = 0;
int storedAmount = 0;
int consumptionRate = 0;
int potentialAmount = 0;

// Large = 0, Small = 1
int gridSize = 0;

/*
 * Constructor
*/
public Program() 
{
    // Output panel
    outputPanel = GridTerminalSystem.GetBlockWithName(TEXT_PANEL_ID) as IMyTextPanel;

    // Size of grid
    gridSize = (int) outputPanel.CubeGrid.GridSizeEnum;
    Echo("Grid Size: " + (gridSize == 1 ? "Small" : "Large"));

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
    GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(cargo);

    // Gas generators
    GridTerminalSystem.GetBlocksOfType<IMyGasGenerator>(generators);
    conversionRate = ICE_TO_H2_CONV_SPEED[gridSize] * generators.Count;

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
    potentialAmount = ICE_TO_H2_CONV_RATIO[gridSize] * (CountMaterialInContainer(cargo, "Ice") + CountMaterialInContainer(generators, "Ice"));

    // Display data on panel
    string outputString = "";
    outputString += "H2 status: " + storedAmount + " + \t" + potentialAmount + "\n"
        + "Change /T: " + consumptionRate + " \t(" + consumptionMaxRate + ") Hps\n";

    int minimumBurnTime = (storedAmount + potentialAmount) / -consumptionMaxRate;

    if (consumptionRate < 0)
    {
        int potentialBurntime = (storedAmount + potentialAmount) / -consumptionRate;
        outputString += "Burn time: " + potentialBurntime + " \t(" + minimumBurnTime + ")s\n";

        if (consumptionRate < -conversionRate && generators.Count > 0)
        {
            outputString += "Not enough hydrogen generation.\nEngines may fail after ~" + (-storedAmount / consumptionRate) + " seconds!";
        }
    } else
    {
        outputString += "Burn time: ~ \t(" + minimumBurnTime + ")s\n";
    }

    outputPanel.WritePublicText(outputString);
}

/*
 * Count selected material in supplied terminal blocks
*/
public int CountMaterialInContainer(List<IMyTerminalBlock> blocks, string filter)
{
    int count = 0;
    for (int i = 0; i < blocks.Count; i++)
    {
        IMyEntity containerOwner = blocks[i] as IMyEntity;
        List<IMyInventoryItem> containerContent = containerOwner.GetInventory(0).GetItems();
        for (int j = 0; j < containerContent.Count; j++)
        {
            if (containerContent[j].Content.SubtypeName.Equals(filter))
            {
                count += containerContent[j].Amount.ToIntSafe();
            }
        }
    }

    return count;
}
