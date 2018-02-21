public class HydrogenNet
{
    private class IMyThrust_Wrapper
    {
        public IMyThrust Block;
        public int Vec, Size;

        public IMyThrust_Wrapper(IMyThrust block, int vec)
        {
            Block = block;
            Vec = vec;
            Size = block.BlockDefinition.SubtypeName.Contains("LargeHydrogen") ? 1 : 0;
        }
    }

    private static Matrix IDENTITY = new Matrix(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);

    private static MyDefinitionId GAS_HYDROGEN_DEFID = MyDefinitionId.Parse("MyObjectBuilder_GasProperties/Hydrogen");
    private static MyDefinitionId ORE_ICE_DEFID = MyDefinitionId.Parse("MyObjectBuilder_Ore/Ice");
    private static string[] THRUST_HYDROGEN_SUBIDS = {
        "LargeBlockLargeHydrogenThrust",
        "LargeBlockSmallHydrogenThrust",
        "SmallBlockLargeHydrogenThrust",
        "SmallBlockSmallHydrogenThrust"
    };

    private static string IGNORE_STRING = "#IGNORE";

    private static double[] H_THRUSTER_DRAW = { 1092, 6426, 109, 514 };
    private static double[] ICE_PER_SECOND = { 167, 83 };
    private static double ICE_TO_HYDROGEN_RATIO = 10;

    private List<IMyTerminalBlock> temp = new List<IMyTerminalBlock>();

    private Program _program;
    private int _gridSize;
    private bool _controlled;

    public double GasToProduce = 0;
    public double GasFillLastRatio = 0;
    public double GasFillRatio = 0;
    public double GasProduction = 0;
    public double GasCapacity = 0;
    public double ThrusterDraw = 0;
    public double ThrusterDrawMax = 0;
    public double[] ThrusterVectorDraw = new double[6];
    public double[] ThrusterVectorDrawMax = new double[6];

    private List<IMyThrust_Wrapper> _thrusters = new List<IMyThrust_Wrapper>();
    private List<IMyInventory> _generatorInventories = new List<IMyInventory>();
    private List<IMyInventory> _cargoInventories = new List<IMyInventory>();
    private List<IMyGasGenerator> _gasGenerators = new List<IMyGasGenerator>();
    private List<IMyGasTank> _gasTanks = new List<IMyGasTank>();

    public HydrogenNet(Program program)
    {
        _program = program;
        _gridSize = (int) program.Me.CubeGrid.GridSizeEnum;
        ScanGrid();
    }

    public void ScanGrid()
    {
        _thrusters.Clear();
        _generatorInventories.Clear();
        _cargoInventories.Clear();
        _gasGenerators.Clear();
        _gasTanks.Clear();

        _program.GridTerminalSystem.GetBlocksOfType(_gasGenerators, b => IsValid(b));
        _program.GridTerminalSystem.GetBlocksOfType(_gasTanks, b => IsValid(b) && CanAcceptResource(b, GAS_HYDROGEN_DEFID));
        _program.GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(temp, b => IsValid(b));
        GetInventory(_generatorInventories, _gasGenerators);
        GetInventory(_cargoInventories, temp);

        Matrix matrix = IDENTITY;
        _controlled = false;

        _program.GridTerminalSystem.GetBlocksOfType<IMyShipController>(temp, c => IsValid(c));
        if (temp.Count > 0) {
            for (int i = 0; i < temp.Count; i++) {
                if ((temp[i] as IMyShipController).IsMainCockpit) {
                    temp[i].Orientation.GetMatrix(out matrix);
                    _controlled = true;
                    break;
                }
            }

            if (!_controlled) {
                _controlled = true;
                temp[0].Orientation.GetMatrix(out matrix);
            }

            Matrix.Transpose(ref matrix, out matrix);
        }

        ThrusterDrawMax = 0;
        Array.Clear(ThrusterVectorDrawMax, 0, 6);

        double draw = 0;
        _program.GridTerminalSystem.GetBlocksOfType<IMyThrust>(temp, b => IsValid(b) && THRUST_HYDROGEN_SUBIDS.Contains(b.BlockDefinition.SubtypeName));
        for (int i = 0; i < temp.Count; i++) {
            int direction = -1;

            if (_controlled) {
                Matrix thrust;
                temp[i].Orientation.GetMatrix(out thrust);
                Vector3 vector = Vector3.Transform(thrust.Backward, matrix);

                if (vector == IDENTITY.Backward) {
                    direction = 0;
                } else if (vector == IDENTITY.Forward) {
                    direction = 1;
                } else if (vector == IDENTITY.Right) {
                    direction = 2;
                } else if (vector == IDENTITY.Left) {
                    direction = 3;
                } else if (vector == IDENTITY.Down) {
                    direction = 4;
                } else if (vector == IDENTITY.Up) {
                    direction = 5;
                }
            }

            IMyThrust_Wrapper wrapper = new IMyThrust_Wrapper(temp[i] as IMyThrust, direction);
            draw = H_THRUSTER_DRAW[2 * _gridSize + wrapper.Size];
            ThrusterDrawMax += draw;

            if (_controlled) {
                ThrusterVectorDrawMax[direction] += draw;
            }

            _thrusters.Add(wrapper);
        }

        GasCapacity = 0;
        _gasTanks.ForEach(b => GasCapacity += b.Capacity);
        GasProduction = _gasGenerators.Count * ICE_PER_SECOND[_gridSize] * ICE_TO_HYDROGEN_RATIO;
    }

    public void Update()
    {
        GasFillLastRatio = GasFillRatio;

        GasFillRatio = 0;
        _gasTanks.ForEach(b => GasFillRatio += b.FilledRatio);
        GasFillRatio /= _gasTanks.Count;

        GasToProduce = ICE_TO_HYDROGEN_RATIO * (GetItemCount(_generatorInventories, ORE_ICE_DEFID) + GetItemCount(_cargoInventories, ORE_ICE_DEFID));

        ThrusterDraw = 0;
        Array.Clear(ThrusterVectorDraw, 0, 6);

        double draw = 0;
        foreach (IMyThrust_Wrapper wrapper in _thrusters) {
            draw = wrapper.Block.CurrentThrust / wrapper.Block.MaxThrust * H_THRUSTER_DRAW[2 * _gridSize + wrapper.Size];

            if (_controlled) {
                ThrusterVectorDraw[wrapper.Vec] += draw;
            }

            ThrusterDraw += draw;
        }
    }

    private void GetInventory<T>(List<IMyInventory> invs, List<T> blocks) where T : IMyTerminalBlock
    {
        invs.Clear();
        blocks.ForEach(b => {
            for (int i = 0; i < b.InventoryCount; i++) {
                invs.Add(b.GetInventory(i));
            }
        });
    }

    private int GetItemCount(List<IMyInventory> invs, MyDefinitionId filter)
    {
        VRage.MyFixedPoint count = 0;
        invs.ForEach(inv => inv.GetItems().ForEach(i => count += (i.GetDefinitionId() == filter ? i.Amount : 0)));
        return count.ToIntSafe();
    }

    private static bool CanAcceptResource(IMyGasTank block, MyDefinitionId defId)
    {
        MyResourceSinkComponent sink = block.Components.Get<MyResourceSinkComponent>();
        return (sink != null ? sink.AcceptedResources.Any(r => r == defId) : false);
    }

    private bool IsValid(IMyTerminalBlock b) { return b.IsWorking && !b.CustomName.Contains(IGNORE_STRING) && b.CubeGrid == _program.Me.CubeGrid; }

    public bool IsControlled { get { return _controlled; } }
}

public HydrogenNet net;

public List<IMyTextPanel>[] panels = new List<IMyTextPanel>[]{
    new List<IMyTextPanel>(),
    new List<IMyTextPanel>(),
    new List<IMyTextPanel>(),
    new List<IMyTextPanel>()
};

public FancyLCD lcd0 = new FancyLCD();
public FancyLCD lcd1 = new FancyLCD();
public FancyLCD lcd2 = new FancyLCD();
public FancyLCD lcd3 = new FancyLCD();

public void AddPanel(IMyTextPanel panel)
{
    bool addToAll = panel.CustomData.Contains("[H_ALL]");

    if (addToAll || panel.CustomData.Contains("[H_AMOUNT]")) {
        panels[0].Add(panel);
    }

    if (addToAll || panel.CustomData.Contains("[H_PRODUCTION]")) {
        panels[1].Add(panel);
    }

    if (addToAll || panel.CustomData.Contains("[H_CONSUMPTION]")) {
        panels[2].Add(panel);
    }

    if (addToAll || panel.CustomData.Contains("[H_THRUSTER]")) {
        panels[3].Add(panel);
    }

    panel.Font = "Monospace";
    panel.FontSize = 1.0F;
}

public Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.Update10 | UpdateFrequency.Update100;

    List<IMyTextPanel> blocks = new List<IMyTextPanel>();
    GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(blocks);
    blocks.ForEach(b => AddPanel(b));

    net = new HydrogenNet(this);
}

public void Main(string argument, UpdateType updateType)
{
    if ((updateType & UpdateType.Terminal) != 0) {
        net.ScanGrid();
        net.Update();
        ShowInfoOnPanels();
    }

    if ((updateType & UpdateType.Update100) != 0) {
        net.ScanGrid();
    }

    if ((updateType & UpdateType.Update10) != 0) {
        net.Update();
        ShowInfoOnPanels();
    }
}

public class FancyLCD
{
    private StringBuilder m_content = new StringBuilder();

    public void AddLine(string text)
    {
        m_content.Append(text);
        m_content.Append('\n');
    }

    public void AddBar(double ratio)
    {
        for (int i = 0; i < 26; i++) {
            m_content.Append(i < 26 * ratio ? "█" : "_");
        }

        m_content.Append('\n');
    }

    public static int GetScale1K(int size, double value)
    {
        int scale = 0;
        for (int i = 0; i < size; i++) {
            if (value > 10 * Math.Pow(1000, scale + 1)) {
                scale++;
            } else {
                break;
            }
        }

        return scale;
    }

    public void Clear() { m_content.Clear(); }
    public static double GetScaled1K(int mod, double value) { return value / Math.Pow(1000, mod); }

    public StringBuilder Content { get { return m_content; } }
}

public void ShowInfoOnPanels()
{
    panels[0].ForEach(p => p.WritePublicText(""));
    panels[1].ForEach(p => p.WritePublicText(""));
    panels[2].ForEach(p => p.WritePublicText(""));
    panels[3].ForEach(p => p.WritePublicText(""));

    if (panels[0].Count > 0 && net.GasCapacity > 0) {
        lcd0.Clear();

        lcd0.AddLine("[        HYDROGEN        ]");
        lcd0.AddBar(net.GasFillRatio);

        int mod0 = FancyLCD.GetScale1K(4, net.GasFillRatio * net.GasCapacity);
        int mod1 = FancyLCD.GetScale1K(4, net.GasCapacity);

        lcd0.Content.AppendFormat("STR: {0,4} {1}L MAX: {2,4} {3}L\n\n", (int) FancyLCD.GetScaled1K(mod0, net.GasFillRatio * net.GasCapacity), " kMGT"[mod0], (int) FancyLCD.GetScaled1K(mod1, net.GasCapacity), " kMGT"[mod1]);

        panels[0].ForEach(p => p.WritePublicText(lcd0.Content, true));
    }

    if (panels[1].Count > 0 && net.GasProduction != 0) {
        lcd1.Clear();

        lcd1.AddLine("[       PRODUCTION       ]");

        int mod0 = FancyLCD.GetScale1K(4, net.GasToProduce);
        int mod1 = FancyLCD.GetScale1K(4, net.GasProduction);

        TimeSpan rem = TimeSpan.FromSeconds(net.GasToProduce / net.GasProduction);
        TimeSpan fil = TimeSpan.FromSeconds((net.GasCapacity > 0) ? net.GasCapacity * (1 - net.GasFillRatio) / net.GasProduction : 0);

        lcd1.Content.AppendFormat("ICE: {0,4} {1}L      {2,5}\n", (int) FancyLCD.GetScaled1K(mod0, net.GasToProduce), " kMGT"[mod0], rem.ToString(@"hh\:mm\.ss"));
        lcd1.Content.AppendFormat("CVR: {0,4} {1}Ls     {2,5}\n\n", (int) FancyLCD.GetScaled1K(mod1, net.GasProduction), " kMGT"[mod1], (net.GasCapacity > 0) ? fil.ToString(@"hh\:mm\.ss") : "");

        panels[1].ForEach(p => p.WritePublicText(lcd1.Content, true));
    }

    if (panels[2].Count > 0 && net.ThrusterDrawMax != 0) {
        lcd2.Clear();

        lcd2.AddLine("[   HYDROGEN THRUSTERS   ]");

        int mod0 = FancyLCD.GetScale1K(4, net.ThrusterDraw);
        int mod1 = FancyLCD.GetScale1K(4, net.ThrusterDrawMax);

        lcd2.AddBar(net.ThrusterDraw / net.ThrusterDrawMax);

        TimeSpan curr = TimeSpan.Zero;
        TimeSpan minm = TimeSpan.Zero;

        if (net.GasCapacity > 0) {
            curr += TimeSpan.FromSeconds((net.ThrusterDraw < 50) ? 0 : net.GasFillRatio * net.GasCapacity / net.ThrusterDraw);
            minm += TimeSpan.FromSeconds(net.GasFillRatio * net.GasCapacity / net.ThrusterDrawMax);
        }

        if (net.GasProduction > 0 && net.GasToProduce > 0) {
            curr += TimeSpan.FromSeconds((net.ThrusterDraw < 50) ? 0 : net.GasToProduce / net.ThrusterDraw);
            minm += TimeSpan.FromSeconds(net.GasToProduce / net.ThrusterDrawMax);
        }

        lcd2.Content.AppendFormat("STR: {0,4} {1}Ls     {2,5}\n", (int) FancyLCD.GetScaled1K(mod0, net.ThrusterDraw), " kMGT"[mod0], curr.ToString(@"hh\:mm\.ss"));
        lcd2.Content.AppendFormat("MAX: {0,4} {1}Ls     {2,5}\n\n", (int) FancyLCD.GetScaled1K(mod1, net.ThrusterDrawMax), " kMGT"[mod1], minm.ToString(@"hh\:mm\.ss"));

        panels[2].ForEach(p => p.WritePublicText(lcd2.Content, true));
    }

    if (panels[3].Count > 0 && net.ThrusterDrawMax != 0) {
        lcd3.Clear();

        lcd3.AddLine("[     VECTOR THRUSTS     ]\n");
        if (net.IsControlled) {
            for (int i = 7; i >= 0; i--) {
                for (int j = 0; j < 6; j++) {
                    int value = (int) (16 * net.ThrusterVectorDraw[j] / net.ThrusterVectorDrawMax[j]);
                    lcd3.Content.AppendFormat("  {0}", (i * 2 < value ? (i * 2 + 1 < value ? "██" : "▄▄") : "  "));
                }

                lcd3.Content.Append('\n');
            }

            lcd3.AddLine("\n  FS  BS  LT  RT  UP  DN");
        } else {
            lcd3.AddLine("  NO CONTROLLER LOCATED\n");
        }

        panels[3].ForEach(p => p.WritePublicText(lcd3.Content, true));
    }
}
