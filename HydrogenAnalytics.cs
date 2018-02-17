#region hydrogen

public static string[] MARKERS = { "#H_STAT", "#H_THRUST" };
public static string DIR = "FBLRUD";

public List<IMyTextPanel> panel0 = new List<IMyTextPanel>();
public List<IMyTextPanel> panel1 = new List<IMyTextPanel>();

public class MyHydrogenThruster
{
    private static string LARGE_VAR_MARK = "LargeHydrogen";

    private IMyThrust block;
    private int thrustVector;
    private bool largeVariant;

    public MyHydrogenThruster(IMyThrust block, int thrustVector)
    {
        this.block = block;
        this.thrustVector = thrustVector;
        this.largeVariant = block.BlockDefinition.SubtypeName.Contains(LARGE_VAR_MARK);
    }

    public IMyThrust Block { get { return block; } }
    public int Direction { get { return thrustVector; } }
    public bool Large { get { return largeVariant; } }
}

public class Grid
{
    private static Matrix IDENTITY = new Matrix(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);

    private List<IMyInventory> invInt = new List<IMyInventory>();
    private List<IMyInventory> invExt = new List<IMyInventory>();

    private List<IMyGasGenerator> gasGen = new List<IMyGasGenerator>();
    private List<IMyGasTank> gasTank = new List<IMyGasTank>();

    private List<MyHydrogenThruster> thrust = new List<MyHydrogenThruster>();

    private bool controlled;

    private static int[] RATIO_ICE_TO_H2 = { 9, 4 };
    private static int[] SPEED_ICE_TO_H2 = { 501, 166 };
    private static int[] H_THRUSTER_DRAW = { 1092, 6426, 109, 514 };

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

    private IMyGridTerminalSystem system;
    private MyCubeSize size;

    public Grid(IMyGridTerminalSystem system, MyCubeSize size)
    {
        this.system = system;
        this.size = size;
    }

    public void Detect()
    {
        storageVariation = 0;
        storageSize = 0;
        storageFillPercentage = 0;
        processRate = 0;
        storageExternal = 0;
        thrusterDraw = 0;
        thrusterMaxDraw = 0;

        vectorThrustDraw = new double[6] { 0, 0, 0, 0, 0, 0 };
        vectorThrustMaxDraw = new double[6] { 0, 0, 0, 0, 0, 0 };

        invInt.Clear();
        invExt.Clear();
        gasGen.Clear();
        gasTank.Clear();
        thrust.Clear();

        system.GetBlocksOfType<IMyGasTank>(gasTank, b => IsValid(b, "Hydro"));
        system.GetBlocksOfType<IMyGasGenerator>(gasGen, b => IsValid(b));

        List<IMyTerminalBlock> cargo = new List<IMyTerminalBlock>();
        system.GetBlocksOfType<IMyCargoContainer>(cargo, b => IsValid(b));

        GetInventory(ref invInt, ref gasGen);
        GetInventory(ref invExt, ref cargo);

        Matrix matrix = IDENTITY;
        controlled = false;

        List<IMyShipController> control = new List<IMyShipController>();
        system.GetBlocksOfType<IMyShipController>(control, c => IsValid(c));

        if (control.Count > 0) {
            for (int i = 0; i < control.Count; i++) {
                if (control[i].IsMainCockpit) {
                    control[i].Orientation.GetMatrix(out matrix);
                    controlled = true;
                    break;
                }
            }

            if (!controlled) {
                control[0].Orientation.GetMatrix(out matrix);
                controlled = true;
            }

            Matrix.Transpose(ref matrix, out matrix);
        }

        List<IMyThrust> blocks = new List<IMyThrust>();
        system.GetBlocksOfType<IMyThrust>(blocks, b => IsValid(b, "Hydrogen"));

        for (int i = 0; i < blocks.Count; i++) {
            int direction = -1;

            if (controlled) {
                Matrix thruster;
                blocks[i].Orientation.GetMatrix(out thruster);
                Vector3 vector = Vector3.Transform(thruster.Backward, matrix);

                if (vector == IDENTITY.Down) {
                    direction = 0;
                } else if (vector == IDENTITY.Up) {
                    direction = 1;
                } else if (vector == IDENTITY.Right) {
                    direction = 2;
                } else if (vector == IDENTITY.Left) {
                    direction = 3;
                } else if (vector == IDENTITY.Backward) {
                    direction = 4;
                } else if (vector == IDENTITY.Forward) {
                    direction = 5;
                }
            }

            MyHydrogenThruster hydroThruster = new MyHydrogenThruster(blocks[i], direction);
            thrust.Add(hydroThruster);

            int draw = H_THRUSTER_DRAW[2 * GridSize + (hydroThruster.Large ? 1 : 0)];
            if (controlled) {
                vectorThrustMaxDraw[direction] += draw;
            }

            thrusterMaxDraw += draw;
        }

        gasTank.ForEach(t => storageSize += t.Capacity);
        processRate = SPEED_ICE_TO_H2[GridSize] * gasGen.Count;
    }

    public void Update()
    {
        storageLast = storageFillPercentage * storageSize;
        storageFillPercentage = 0;
        gasTank.ForEach(block => storageFillPercentage += block.FilledRatio);
        storageFillPercentage /= gasTank.Count;
        storageVariation = storageFillPercentage * storageSize - storageLast;
        storageExternal = RATIO_ICE_TO_H2[GridSize] * (GetItemCount(invExt, "Ice") + GetItemCount(invInt, "Ice"));

        vectorThrustDraw = new double[6] { 0, 0, 0, 0, 0, 0 };

        thrusterDraw = 0;
        for (int i = 0; i < thrust.Count; i++) {
            IMyThrust thruster = thrust[i].Block;
            float thrusterDraw = (thruster.CurrentThrust / thruster.MaxThrust) * H_THRUSTER_DRAW[2 * GridSize + (thrust[i].Large ? 1 : 0)];

            if (controlled) {
                vectorThrustDraw[thrust[i].Direction] += thrusterDraw;
            }

            this.thrusterDraw += thrusterDraw;
        }
    }

    private int GetItemCount(List<IMyInventory> inventories, string filter)
    {
        int count = 0;

        inventories.ForEach(inventory => {
            inventory.GetItems().ForEach(item => {
                if (item.Content.SubtypeName.Equals(filter)) {
                    count += item.Amount.ToIntSafe();
                }
            });
        });

        return count;
    }

    void GetInventory<T>(ref List<IMyInventory> invs, ref List<T> blocks) where T : IMyTerminalBlock
    {
        for (int i = 0; i < blocks.Count; i++) {
            for (int j = 0; j < blocks[i].InventoryCount; j++) {
                invs.Add(blocks[i].GetInventory(j));
            }
        }
    }

    private bool IsValid(IMyTerminalBlock b) { return b.IsWorking && b.IsFunctional && !b.CustomName.Contains("#IGNORE"); }
    private bool IsValid(IMyTerminalBlock b, string filter) { return IsValid(b) && b.CustomName.Contains(filter); }

    public List<IMyInventory> InventoryExt { get { return invExt; } }
    public List<IMyInventory> InventoryInt { get { return invInt; } }
    public List<MyHydrogenThruster> Thrusters { get { return thrust; } }
    public List<IMyGasGenerator> Generators { get { return gasGen; } }
    public List<IMyGasTank> Tanks { get { return gasTank; } }
    public int GridSize { get { return (int) size; } }
    public bool HasController { get { return controlled; } }

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
    for (int i = 0; i < blocks.Count; i++) {
        if (blocks[i] is T) {
            filteredBlocks.Add((T) blocks[i]);
        }
    }

    return filteredBlocks;
}

public void ShowInfoOnPanels()
{
    if (panel0.Count > 0) {
        StringBuilder content = new StringBuilder();

        if (grid.StorageSize != 0) {
            content.AppendFormat("Hydrogen Status [H]\n\nStored H2: {0,-5} {1,6}%\n", (int) grid.StorageInternal, (int) (grid.StorageFillPercentage * 100));
            if (grid.ProcessRate != 0) {
                content.AppendFormat("Extern H2: {0,-5}\n", (int) grid.StorageExternal);
            }
        }

        if (grid.ProcessRate != 0 || grid.ThrusterMaxDraw != 0) {
            content.Append("\nRates [Hps]\n\n");
            if (grid.ProcessRate != 0) {
                content.AppendFormat("Max gain: {0,-5}\n", (int) grid.ProcessRate);
            }

            if (grid.ThrusterMaxDraw != 0) {
                content.AppendFormat("Max draw: {0,-5}\n", (int) grid.ThrusterMaxDraw);
            }
        }

        if (grid.ThrusterMaxDraw != 0) {
            content.Append("\nThrust lengths [s]\n\n      Source   Time [s]\n");
            if (grid.StorageSize != 0) {
                content.AppendFormat("Min   Tanks    {0,-5}\n", (int) (grid.StorageInternal / grid.ThrusterMaxDraw));
                if (grid.ThrusterDraw != 0) {
                    content.AppendFormat("Apx   Tanks    {0,-5}\n", (int) (grid.StorageInternal / grid.ThrusterDraw));
                }
            }

            if (grid.ProcessRate != 0) {
                content.AppendFormat("Min   Any      {0,-5}\n", (int) ((grid.StorageInternal + grid.StorageExternal) / grid.ThrusterMaxDraw));
                if (grid.ThrusterDraw != 0) {
                    content.AppendFormat("Apx   Any      {0,-5}\n", (int) ((grid.StorageInternal + grid.StorageExternal) / grid.ThrusterDraw));
                }
            }
        }
        for (int i = 0; i < panel0.Count; i++) {
            panel0[i].WritePublicText(content);
        }
    }

    if (panel1.Count > 0) {
        StringBuilder content = new StringBuilder();

        if (grid.ThrusterMaxDraw != 0 && grid.HasController) {
            content.AppendFormat("Dir %    Hps    Maximum\n\n");
            for (int i = 5; i >= 0; i--) {
                if (grid.ThrusterVectorMaxDraw[i] != 0) {
                    content.AppendFormat("{0}   {1,-5}{2,-5}   {3,-5}\n", DIR[5 - i], (int) (100 * grid.ThrusterVectorDraw[i] / grid.ThrusterVectorMaxDraw[i]), (int) grid.ThrusterVectorDraw[i], (int) grid.ThrusterVectorMaxDraw[i]);
                } else {
                    content.AppendFormat("{0}        No data\n", DIR[5 - i]);
                }
            }

            content.AppendFormat("\n         {0,-5}   {1,-5}\n", (int) grid.ThrusterDraw, (int) grid.ThrusterMaxDraw);
        } else {
            content.Append("No hydrogen thrusters detected!\n\nNote that you need to \nhave any type of cockpit on \nthe grid to gain access \nto thruster data!");
        }

        for (int i = 0; i < panel1.Count; i++) {
            panel1[i].WritePublicText(content);
        }
    }
}

public Grid grid;

public Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.Update10 | UpdateFrequency.Update100;

    List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.SearchBlocksOfName(MARKERS[0], blocks);
    panel0 = FilterBlockType<IMyTextPanel, IMyTerminalBlock>(blocks);
    blocks.Clear();
    GridTerminalSystem.SearchBlocksOfName(MARKERS[1], blocks);
    panel1 = FilterBlockType<IMyTextPanel, IMyTerminalBlock>(blocks);

    grid = new Grid(GridTerminalSystem, Me.CubeGrid.GridSizeEnum);
    grid.Detect();
}

public void Main(string argument, UpdateType updateType)
{
    if ((updateType & UpdateType.Terminal) != 0) {
        grid.Detect();
        grid.Update();
        ShowInfoOnPanels();
    }

    if ((updateType & UpdateType.Update100) != 0) {
        grid.Detect();
    }

    if ((updateType & UpdateType.Update10) != 0) {
        grid.Update();
        ShowInfoOnPanels();
    }
}

#endregion
