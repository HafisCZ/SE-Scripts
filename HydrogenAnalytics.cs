public class HydrogenNet
{
    private class IMyThrust_Wrapper
    {
        public IMyThrust block;
        public int vec;
        public int size;

        public IMyThrust_Wrapper(IMyThrust block, int vec)
        {
            this.block = block;
            this.vec = vec;
            size = block.BlockDefinition.SubtypeName.Contains("LargeH") ? 1 : 0;
        }
    }

    private static Matrix IDENTITY = new Matrix(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);

    private static double ICE_TO_HYDROGEN_RATIO = 10;
    private static double[] ICE_PER_SECOND = { 167, 83 };
    private static double[] H_THRUSTER_DRAW = { 1092, 6426, 109, 514 };

    private List<IMyTerminalBlock> temp = new List<IMyTerminalBlock>();

    private Program _program;

    public double GasToProduce = 0;
    public double GasFillRatioDelta = 0;
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

    private int _gridSize;
    private bool _controlled;

    public HydrogenNet(Program program)
    {
        _program = program;

        try {
            _gridSize = (int) program.Me.CubeGrid.GridSizeEnum;
            ScanGrid();
        } catch (Exception e) {
            program.Echo(e.StackTrace);
        }
    }

    public void ScanGrid()
    {
        _thrusters.Clear();
        _generatorInventories.Clear();
        _cargoInventories.Clear();
        _gasGenerators.Clear();
        _gasTanks.Clear();

        _program.GridTerminalSystem.GetBlocksOfType(_gasGenerators, b => IsValid(b));
        _program.GridTerminalSystem.GetBlocksOfType(_gasTanks, b => IsValid(b, "Hydrogen"));
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
        for (int i = 0; i < 6; i++) {
            ThrusterVectorDrawMax[i] = 0;
        }

        _program.GridTerminalSystem.GetBlocksOfType<IMyThrust>(temp, b => IsValid(b, "Hydrogen"));
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

            IMyThrust_Wrapper thruster = new IMyThrust_Wrapper(temp[i] as IMyThrust, direction);
            _thrusters.Add(thruster);

            double draw = H_THRUSTER_DRAW[2 * _gridSize + thruster.size];

            if (_controlled) {
                ThrusterVectorDrawMax[direction] += draw;
            }

            ThrusterDrawMax += draw;
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

        GasFillRatioDelta = GasFillRatio - GasFillLastRatio;
        GasToProduce = ICE_TO_HYDROGEN_RATIO * (GetItemCount(_generatorInventories, "Ice") + GetItemCount(_cargoInventories, "Ice"));

        ThrusterDraw = 0;
        for (int i = 0; i < 6; i++) {
            ThrusterVectorDraw[i] = 0;
        }

        foreach (IMyThrust_Wrapper wrapper in _thrusters) {
            IMyThrust thruster = wrapper.block;

            double draw = thruster.CurrentThrust / thruster.MaxThrust * H_THRUSTER_DRAW[2 * _gridSize + wrapper.size];
            if (_controlled) {
                ThrusterVectorDraw[wrapper.vec] += draw;
            }

            ThrusterDraw += draw;
        }
    }

    private void GetInventory<T>(List<IMyInventory> inventories, List<T> blocks) where T : IMyTerminalBlock
    {
        inventories.Clear();
        blocks.ForEach(b => {
            for (int i = 0; i < b.InventoryCount; i++) {
                inventories.Add(b.GetInventory(i));
            }
        });
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

    private bool IsValid(IMyTerminalBlock b) { return b.IsWorking && b.IsFunctional && !b.CustomName.Contains("#IGNORE") && b.CubeGrid == _program.Me.CubeGrid; }
    private bool IsValid(IMyTerminalBlock b, string filter) { return IsValid(b) && b.CustomName.Contains(filter); }

    public bool IsControlled { get { return _controlled; } }
}

public HydrogenNet net;

public List<IMyTextPanel>[] panels = new List<IMyTextPanel>[]{
    new List<IMyTextPanel>(),
    new List<IMyTextPanel>(),
    new List<IMyTextPanel>(),
    new List<IMyTextPanel>()
};

public Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.Update10 | UpdateFrequency.Update100;

    List<IMyTextPanel> blocks = new List<IMyTextPanel>();
    GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(blocks);

    blocks.ForEach(b => {
        if (b.CustomData.Contains("[H_AMOUNT]")) {
            panels[0].Add(b);
        }

        if (b.CustomData.Contains("[H_PRODUCTION]")) {
            panels[1].Add(b);
        }

        if (b.CustomData.Contains("[H_CONSUMPTION]")) {
            panels[2].Add(b);
        }

        if (b.CustomData.Contains("[H_THRUSTER]")) {
            panels[3].Add(b);
        }
    });

    for (int i = 0; i < 4; i++) {
        panels[i].ForEach(p => p.Font = "Monospace");
    }

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

public void ShowInfoOnPanels()
{
    StringBuilder content = new StringBuilder();

    for (int i = 0; i < 4; i++) {
        if (panels[i].Count > 0) {
            panels[i].ForEach(p => p.WritePublicText(""));
        }
    }

    if (panels[0].Count > 0) {
        content.Clear();

        if (net.GasCapacity != 0) {
            content.Append("[        HYDROGEN        ]\n");

            int fill = (int) (26 * net.GasFillRatio);
            for (int i = 0; i < 26; i++) {
                content.Append(i < fill ? "█" : "_");
            }

            int mod = 0;
            int amm = (int) (net.GasFillRatio * net.GasCapacity);
            for (int i = 0; i < 4; i++) {
                if (amm > 10 * Math.Pow(1000, mod + 1)) {
                    mod++;
                } else {
                    break;
                }
            }

            int mod2 = 0;
            int amm2 = (int) net.GasCapacity;
            for (int i = 0; i < 4; i++) {
                if (amm2 > 10 * Math.Pow(1000, mod2 + 1)) {
                    mod2++;
                } else {
                    break;
                }
            }

            content.AppendFormat("\nSTR: {0,4} {1}L MAX: {2,4} {3}L\n\n", (int) (amm / Math.Pow(1000, mod)), " kMGT"[mod], (int) (amm2 / Math.Pow(1000, mod2)), " kMGT"[mod2]);
        }

        panels[0].ForEach(p => p.WritePublicText(content, true));
    }

    if (panels[1].Count > 0) {
        content.Clear();

        if (net.GasProduction != 0) {
            content.Append("[       PRODUCTION       ]\n");

            int mod = 0;
            int amm = (int) net.GasToProduce;
            for (int i = 0; i < 4; i++) {
                if (amm > 10 * Math.Pow(1000, mod + 1)) {
                    mod++;
                } else {
                    break;
                }
            }

            int mod2 = 0;
            int amm2 = (int) net.GasProduction;
            for (int i = 0; i < 4; i++) {
                if (amm2 > 10 * Math.Pow(1000, mod2 + 1)) {
                    mod2++;
                } else {
                    break;
                }
            }

            TimeSpan rem = TimeSpan.FromSeconds(net.GasToProduce / net.GasProduction);

            content.AppendFormat("ICE: {0,4} {1}L      {2,5}\n", (int) (amm / Math.Pow(1000, mod)), " kMGT"[mod], rem.ToString(@"hh\:mm\.ss"));
            content.AppendFormat("CVR: {0,4} {1}Ls\n\n", (int) (amm2 / Math.Pow(1000, mod2)), " kMGT"[mod2]);
        }

        panels[1].ForEach(p => p.WritePublicText(content, true));
    }

    if (panels[2].Count > 0) {
        content.Clear();

        if (net.ThrusterDrawMax != 0) {
            content.Append("[   HYDROGEN THRUSTERS   ]\n");

            int mod = 0;
            int amm = (int) net.ThrusterDraw;
            for (int i = 0; i < 4; i++) {
                if (amm > 10 * Math.Pow(1000, mod + 1)) {
                    mod++;
                } else {
                    break;
                }
            }

            int mod2 = 0;
            int amm2 = (int) net.ThrusterDrawMax;
            for (int i = 0; i < 4; i++) {
                if (amm2 > 10 * Math.Pow(1000, mod2 + 1)) {
                    mod2++;
                } else {
                    break;
                }
            }

            int fill = (int) (26 * (net.ThrusterDraw / net.ThrusterDrawMax));
            for (int i = 0; i < 26; i++) {
                content.Append(i < fill ? "█" : "_");
            }

            TimeSpan curr = TimeSpan.FromSeconds(net.ThrusterDraw > 50 ? (net.GasFillRatio * net.GasCapacity + net.GasToProduce) / net.ThrusterDraw : 0);
            TimeSpan minm = TimeSpan.FromSeconds((net.GasFillRatio * net.GasCapacity + net.GasToProduce) / net.ThrusterDrawMax);

            content.AppendFormat("\nSTR: {0,4} {1}Ls     {2,5}\n", (int) (amm / Math.Pow(1000, mod)), " kMGT"[mod], curr.ToString(@"hh\:mm\.ss"));
            content.AppendFormat("MAX: {0,4} {1}Ls     {2,5}\n\n", (int) (amm2 / Math.Pow(1000, mod2)), " kMGT"[mod2], minm.ToString(@"hh\:mm\.ss"));
        }

        panels[2].ForEach(p => p.WritePublicText(content, true));
    }

    if (panels[3].Count > 0) {
        content.Clear();

        if (net.ThrusterDrawMax != 0) {
            content.Append("[     VECTOR THRUSTS     ]\n\n");
            if (net.IsControlled) {
                for (int i = 7; i >= 0; i--) {
                    for (int j = 0; j < 6; j++) {
                        content.AppendFormat("  {0}", (i < (int)(8 * net.ThrusterVectorDraw[j] / net.ThrusterVectorDrawMax[j]) ? "██" : "  "));
                    }

                    content.Append("\n");
                }

                content.Append("\n  FS  BS  LT  RT  UP  DN\n");
            } else {
                content.Append("  NO CONTROLLER LOCATED\n\n");
            }
        }

        panels[3].ForEach(p => p.WritePublicText(content, true));
    }
}
