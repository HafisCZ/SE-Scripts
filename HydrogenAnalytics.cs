/*
 * 0 - #H_STAT   - Current hydrogen status (amounts, burn time)
 * 1 - #H_THRUST - Hydrogen consumption per individual thrust vector
*/
public readonly string[] PANEL_ID = { "#H_STAT", "#H_THRUST" };
public readonly string[] DIRS = { "F", "B", "L", "R", "U", "D"};

public List<IMyTextPanel>[] panels = new List<IMyTextPanel>[2];

public FuelAnalyst analyst;

public class FuelGrid {

    private readonly Vector3I[] DIRECTION =
    {
        new Vector3I(0, 1, 0),
        new Vector3I(0, -1, 0),
        new Vector3I(-1, 0, 0), 
        new Vector3I(1, 0, 0),
        new Vector3I(0, 0, -1), 
        new Vector3I(0, 0, 1)
     };

    public class ThrusterDetailedInfo
    {
        private readonly IMyThrust thruster;
        private readonly int direction;
        private readonly bool largeVariant;

        public ThrusterDetailedInfo(IMyThrust thruster, int direction, bool largeVariant)
        {
            this.thruster = thruster;
            this.direction = direction;
            this.largeVariant = largeVariant;
        }

        public IMyThrust ThrusterBlock { get { return thruster; } }
        public int Direction { get { return direction; } }
        public bool IsLarge { get { return largeVariant; } }
    }

    private List<IMyInventory> contentCargo, contentGen;
    private List<IMyTerminalBlock> generators;
    private List<IMyGasTank> tanks;
    private List<ThrusterDetailedInfo> thrusters;

    private readonly int gridSize;
    private readonly IMyGridTerminalSystem grid;

    public FuelGrid(IMyGridTerminalSystem grid, IMyCubeGrid cube) {
        gridSize = (int)cube.GridSizeEnum;
        this.grid = grid;

        generators = new List<IMyTerminalBlock>();
        tanks = new List<IMyGasTank>();
        thrusters = new List<ThrusterDetailedInfo>();
    }

    public void Detect() {
        if (grid != null) {
            generators.Clear();
            tanks.Clear();
            thrusters.Clear();

            List<IMyThrust> thrusterBlocks = new List<IMyThrust>();
            grid.GetBlocksOfType<IMyThrust>(thrusterBlocks, block => IsValid(block) && block.BlockDefinition.SubtypeId.Contains("HydrogenThrust"));

            for (int i = 0; i < thrusterBlocks.Count; i++)
            {
                for (int j = 0; j < 6; j++)
                {
                    if (thrusterBlocks[i].GridThrustDirection.Equals(DIRECTION[j]))
                    {
                        thrusters.Add(new ThrusterDetailedInfo(thrusterBlocks[i], j, thrusterBlocks[i].BlockDefinition.SubtypeId.Contains("LargeHydrogenThrust")));
                        break;
                    }
                }
            }

            grid.GetBlocksOfType<IMyGasTank>(tanks, block => IsValid(block));

            grid.GetBlocksOfType<IMyGasGenerator>(generators, block => IsValid(block));
            contentGen = GetInventories(generators);

            List<IMyTerminalBlock> cargoBlocks = new List<IMyTerminalBlock>();
            grid.GetBlocksOfType<IMyCargoContainer>(cargoBlocks, block => IsValid(block));
            contentCargo = GetInventories(cargoBlocks);
        }
    }

    public List<IMyInventory> CargoContents { get { return contentCargo; } }
    public List<IMyInventory> GasGeneratorContents { get { return contentGen; } }
    public List<ThrusterDetailedInfo> Thrusters { get { return thrusters; } }
    public List<IMyTerminalBlock> GasGenerators { get { return generators; } }
    public List<IMyGasTank> GasTanks { get { return tanks; } }

    public int GridSize { get { return gridSize; } }

    private bool IsValid(IMyTerminalBlock block) {
        return block.IsWorking && block.IsFunctional;
    }

    private List<IMyInventory> GetInventories(List<IMyTerminalBlock> blocks) {
        List<IMyInventory> inventories = new List<IMyInventory>();
        for (int i = 0; i < blocks.Count; i++) {
            for (int j = 0; j < blocks[i].InventoryCount; j++) {
                inventories.Add(blocks[i].GetInventory(j));
            }
        }

        return inventories;
    }
}

public class FuelAnalyst
{

    public readonly int[] RATIO_ICE_TO_H2 = { 9, 4 };
    public readonly int[] SPEED_ICE_TO_H2 = { 501, 166 };
    public readonly int[] H_THRUSTER_DRAW = { 1092, 6426, 109, 514 };

    private double storageLast;

    private double storageVariation;
    private double storageSize;
    private double storageFillPercentage;
    private double processRate;
    private double storageExternal;
    private double thrusterDraw;
    private double thrusterMaxDraw;

    private double[] vectorThrustDraw;
    private double[] vectorThrustMaxDraw;

    private readonly FuelGrid fuelGrid;

    public FuelAnalyst(FuelGrid fuelGrid)
    {
        this.fuelGrid = fuelGrid;

        Detect();
    }

    public void Detect()
    {
        if (fuelGrid != null)
        {
            fuelGrid.Detect();

            storageVariation = 0;
            storageSize = 0;
            storageFillPercentage = 0;
            processRate = 0;
            storageExternal = 0;
            thrusterDraw = 0;
            thrusterMaxDraw = 0;

            vectorThrustDraw = new double[6] { 0, 0, 0, 0, 0, 0 };
            vectorThrustMaxDraw = new double[6] { 0, 0, 0, 0, 0, 0 };

            fuelGrid.GasTanks.ForEach(tank => storageSize += tank.Capacity);
            processRate = SPEED_ICE_TO_H2[fuelGrid.GridSize] * fuelGrid.GasGenerators.Count;

            for (int i = 0; i < fuelGrid.Thrusters.Count; i++)
            {
                int thrusterDraw = H_THRUSTER_DRAW[2 * fuelGrid.GridSize + (fuelGrid.Thrusters[i].IsLarge ? 1 : 0)];
                vectorThrustMaxDraw[fuelGrid.Thrusters[i].Direction] += thrusterDraw;

                thrusterMaxDraw += thrusterDraw;
            }
        }
    }

    public void Update()
    {
        storageLast = storageFillPercentage * storageSize;
        storageFillPercentage = 0;

        fuelGrid.GasTanks.ForEach(block => storageFillPercentage += block.FilledRatio);
        storageFillPercentage /= fuelGrid.GasTanks.Count;

        storageVariation = storageFillPercentage * storageSize - storageLast;
        storageExternal = RATIO_ICE_TO_H2[fuelGrid.GridSize] * (GetItemCount(fuelGrid.CargoContents, "Ice") + GetItemCount(fuelGrid.GasGeneratorContents, "Ice"));

        for (int i = 0; i < 6; i++)
        {
            vectorThrustDraw[i] = 0;
        }

        thrusterDraw = 0;
        for (int i = 0; i < fuelGrid.Thrusters.Count; i++)
        {
            IMyThrust thruster = fuelGrid.Thrusters[i].ThrusterBlock;
            float thrusterDraw = (thruster.CurrentThrust / thruster.MaxThrust) * H_THRUSTER_DRAW[2 * fuelGrid.GridSize + (fuelGrid.Thrusters[i].IsLarge ? 1 : 0)];

            vectorThrustDraw[fuelGrid.Thrusters[i].Direction] += thrusterDraw;
            this.thrusterDraw += thrusterDraw;
        }
    }

    private int GetItemCount(List<IMyInventory> inventories, string filter)
    {
        int count = 0;

        inventories.ForEach(inventory =>
        {
            inventory.GetItems().ForEach(item =>
            {
                if (item.Content.SubtypeName.Equals(filter))
                {
                    count += item.Amount.ToIntSafe();
                }
            });
        });

        return count;
    }

    public double StorageVariation { get { return storageVariation; } }
    public double StorageSize { get { return storageSize; } }
    public double StorageFillPercentage { get { return storageFillPercentage; } }
    public double StorageExternal { get { return storageExternal; } }
    public double StorageInternal { get { return storageFillPercentage * storageSize; } }
    public double ProcessRate { get { return processRate; } }
    public double ThrusterMaxDraw { get { return thrusterMaxDraw; } }
    public double ThrusterDraw { get { return thrusterDraw; } }
    public double[] ThrusterVectorDraw { get { return vectorThrustDraw; } }
    public double[] ThrusterVectorMaxDraw { get { return vectorThrustMaxDraw; } }

} 

public List<T> FilterBlockType<T, U>(IList<U> blocks) where U : IMyCubeBlock where T : U
{
    List<T> filteredBlocks = new List<T>();
    for (int i = 0; i < blocks.Count; i++)
    {
        if (blocks[i] is T)
        {
            filteredBlocks.Add((T)blocks[i]);
        }
    }

    return filteredBlocks;
}

public void ShowInfoOnPanels()
{
    if (panels[0].Count > 0)
    {
        StringBuilder content = new StringBuilder();

        if (analyst.StorageSize != 0)
        {
            content.AppendFormat("Hydrogen Status [H]\n\nStored H2: {0,-5} {1,6}%\n", (int)analyst.StorageInternal, (int)(analyst.StorageFillPercentage * 100));
            if (analyst.ProcessRate != 0)
            {
                content.AppendFormat("Extern H2: {0,-5}\n", (int)analyst.StorageExternal);
            } 
        }

        if (analyst.ProcessRate != 0 || analyst.ThrusterMaxDraw != 0)
        {
            content.Append("\nRates [Hps]\n\n");
            if (analyst.ProcessRate != 0)
            {
                content.AppendFormat("Max gain: {0,-5}\n", (int)analyst.ProcessRate);
            }

            if (analyst.ThrusterMaxDraw != 0)
            {
                content.AppendFormat("Max draw: {0,-5}\n", (int)analyst.ThrusterMaxDraw);
            }
        }

        if (analyst.ThrusterMaxDraw != 0)
        {
            content.Append("\nThrust lengths [s]\n\n      Source   Time [s]\n");
            if (analyst.StorageSize != 0)
            {
                content.AppendFormat("Min   Tanks    {0,-5}\n", (int)(analyst.StorageInternal / analyst.ThrusterMaxDraw));
                if (analyst.ThrusterDraw != 0)
                {
                    content.AppendFormat("Apx   Tanks    {0,-5}\n", (int)(analyst.StorageInternal / analyst.ThrusterDraw));
                }
            }

            if (analyst.ProcessRate != 0)
            {
                content.AppendFormat("Min   Any      {0,-5}\n", (int)((analyst.StorageInternal + analyst.StorageExternal) / analyst.ThrusterMaxDraw));
                if (analyst.ThrusterDraw != 0)
                {
                    content.AppendFormat("Apx   Any      {0,-5}\n", (int)((analyst.StorageInternal + analyst.StorageExternal) / analyst.ThrusterDraw));
                }
            }
        }

        // TODO:

        for (int i = 0; i < panels[0].Count; i++)
        {
            panels[0][i].WritePublicText(content);
        }
    }

    if (panels[1].Count > 0)
    {
        StringBuilder content = new StringBuilder();

        if (analyst.ThrusterMaxDraw != 0)
        {
            content.AppendFormat("Dir %    Hps    Maximum\n\n");
            for (int i = 5; i >= 0; i--)
            {
                if (analyst.ThrusterVectorMaxDraw[i] != 0)
                {
                    content.AppendFormat("{0}   {1,-5}{2,-5}   {3,-5}\n", DIRS[5 - i], (int)(100 * analyst.ThrusterVectorDraw[i] / analyst.ThrusterVectorMaxDraw[i]), (int)analyst.ThrusterVectorDraw[i], (int)analyst.ThrusterVectorMaxDraw[i]);
                }
                else
                {
                    content.AppendFormat("{0}        No data\n", DIRS[5 - i]);
                }
            }

            content.AppendFormat("         {0,-5}   {1,-5}\n", (int)analyst.ThrusterDraw, (int)analyst.ThrusterMaxDraw);
        }
        else
        {
            content.Append("No hydrogen thrusters detected!\n\nNote that you need to \nhave any type of cockpit on \nthe grid to gain access \nto thruster data!");
        }   

        for (int i = 0; i < panels[1].Count; i++)
        {
            panels[1][i].WritePublicText(content);
        }
    }
}

public Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.Update10 | UpdateFrequency.Update100;

    List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
    for (int i = 0; i < PANEL_ID.Length; i++)
    {
        blocks.Clear();
        GridTerminalSystem.SearchBlocksOfName(PANEL_ID[i], blocks);
        panels[i] = FilterBlockType<IMyTextPanel, IMyTerminalBlock>(blocks);
    }

    analyst = new FuelAnalyst(new FuelGrid(GridTerminalSystem, Me.CubeGrid));
    analyst.Detect();
}

public void Main(string argument, UpdateType updateType)
{
    if ((updateType & UpdateType.Terminal) != 0)
    {
        analyst.Detect();
        analyst.Update();
        ShowInfoOnPanels();
    }

    if ((updateType & UpdateType.Update100) != 0)
    {
        analyst.Detect();
    }

    if ((updateType & UpdateType.Update10) != 0)
    {
        analyst.Update();
        ShowInfoOnPanels();
    }
}
