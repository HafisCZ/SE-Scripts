#region hydrogen-analyst

// Use with LCD panels marked as shown below:
// Panel Flag      Description
// #H_STAT         Amounts of stored hydrogen and potential hydrogen from stored ice, maximum gain (from gas generators) a maximum draw (from thrusters), specific burn times
// #H_THRUST       Percentual draw, actual draw and maximum draw per each spatial thrust vector

public readonly string[] PANEL_MARKERS = { "#H_STAT", "#H_THRUST" };
public readonly string DIRECTIONS = "FBLRUD";

public List<IMyTextPanel> Panels0 = new List<IMyTextPanel>();
public List<IMyTextPanel> Panels1 = new List<IMyTextPanel>();

public FuelAnalyst analyst;

public class FuelGrid
{
	private readonly Matrix IMat4D = new Matrix(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);

	public class Thruster
	{
		private readonly IMyThrust thruster;
		private readonly int direction;
		private readonly bool large;

		public Thruster(IMyThrust thruster, int direction, bool large)
		{
			this.thruster = thruster;
			this.direction = direction;
			this.large = large;
		}

		public IMyThrust Block { get { return thruster; } }
		public int Direction { get { return direction; } }
		public bool IsLarge { get { return large; } }
	}

	List<IMyInventory> inventoryInt = new List<IMyInventory>();
	List<IMyInventory> inventoryExt = new List<IMyInventory>();
	List<IMyGasTank> gasTanks = new List<IMyGasTank>();
	List<IMyGasGenerator> gasGens = new List<IMyGasGenerator>();
	List<Thruster> thrusters = new List<Thruster>();

	readonly int gridSize;
	readonly IMyGridTerminalSystem gts;

	public FuelGrid(IMyGridTerminalSystem gts, IMyCubeGrid cg)
	{
		this.gts = gts;
		gridSize = (int)(cg.GridSizeEnum);
	}

	public void Detect()
	{
		gasGens.Clear();
		gasTanks.Clear();
		thrusters.Clear();
		inventoryInt.Clear();
		inventoryExt.Clear();

		Matrix refMat4D = IMat4D;
		List<IMyShipController> control = new List<IMyShipController>();
		gts.GetBlocksOfType<IMyShipController>(control, c => IsValid(c));
		if (control.Count > 0)
		{
			if (control.Count == 1)
			{
				control[0].Orientation.GetMatrix(out refMat4D);
			} else
			{
				for (int i = 0; i < control.Count; i++)
				{
					if (control[i].IsMainCockpit)
					{
						control[i].Orientation.GetMatrix(out refMat4D);
					}
				}
			}

			Matrix.Transpose(ref refMat4D, out refMat4D);
		}

		List<IMyThrust> blocks = new List<IMyThrust>();
		gts.GetBlocksOfType<IMyThrust>(blocks, b => IsValid(b) && b.BlockDefinition.SubtypeId.Contains("Hydrogen"));

		Matrix thrustMat4D;
		Vector3 dirVec;
		for (int i = 0; i < blocks.Count; i++)
		{
			blocks[i].Orientation.GetMatrix(out thrustMat4D);
			dirVec = Vector3.Transform(thrustMat4D.Backward, refMat4D);

			int dir =  0;
			if (dirVec == IMat4D.Down) dir = 1;
			else if (dirVec == IMat4D.Left) dir = 2;
			else if (dirVec == IMat4D.Right) dir = 3;
			else if (dirVec == IMat4D.Forward) dir = 4;
			else if (dirVec == IMat4D.Backward) dir = 5;

			thrusters.Add(new Thruster(blocks[i], dir, blocks[i].BlockDefinition.SubtypeId.Contains("LargeH")));
		}

		gts.GetBlocksOfType<IMyGasTank>(gasTanks, b => IsValid(b));
		gts.GetBlocksOfType<IMyGasGenerator>(gasGens, b => IsValid(b));
		GetInventory<IMyGasGenerator>(ref inventoryInt, ref gasGens);

		List<IMyTerminalBlock> cargo = new List<IMyTerminalBlock>();
		gts.GetBlocksOfType<IMyCargoContainer>(cargo, b => IsValid(b));
		GetInventory<IMyTerminalBlock>(ref inventoryExt, ref cargo);
	}

	public ref List<IMyInventory> InventoryExt { get { return ref inventoryExt; } }
	public ref List<IMyInventory> InventoryInt { get { return ref inventoryInt; } }
	public ref List<Thruster> Thrusters { get { return ref thrusters; } }
	public ref List<IMyGasGenerator> Generators { get { return ref gasGens; } }
	public ref List<IMyGasTank> Tanks { get { return ref gasTanks; } }
	public int GridSize { get { return gridSize; } }

	bool IsValid(IMyTerminalBlock b)
	{
		return b.IsWorking && b.IsFunctional;
	}

	void GetInventory<T>(ref List<IMyInventory> invs, ref List<T> blocks) where T : IMyTerminalBlock
	{
		for (int i = 0; i < blocks.Count; i++)
		{
			for (int j = 0; j < blocks[i].InventoryCount; i++)
			{
				invs.Add(blocks[i].GetInventory(j));
			}
		}
	}
}

public class FuelAnalyst
{

	public readonly int[] RATIO_ICE_TO_H2 = { 9, 4 };
	public readonly int[] SPEED_ICE_TO_H2 = { 501, 166 };
	public readonly int[] H_THRUSTER_DRAW = { 1092, 6426, 109, 514 };

	double storageLast;

	double storageVariation;
	double storageSize;
	double storageFillPercentage;
	double processRate;
	double storageExternal;
	double thrusterDraw;
	double thrusterMaxDraw;

	double[] vectorThrustDraw;
	double[] vectorThrustMaxDraw;

	readonly FuelGrid fuelGrid;

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

			fuelGrid.Tanks.ForEach(tank => storageSize += tank.Capacity);
			processRate = SPEED_ICE_TO_H2[fuelGrid.GridSize] * fuelGrid.Generators.Count;

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
		fuelGrid.Tanks.ForEach(block => storageFillPercentage += block.FilledRatio);
		storageFillPercentage /= fuelGrid.Tanks.Count;
		storageVariation = storageFillPercentage * storageSize - storageLast;
		storageExternal = RATIO_ICE_TO_H2[fuelGrid.GridSize] * (GetItemCount(fuelGrid.InventoryExt, "Ice") + GetItemCount(fuelGrid.InventoryInt, "Ice"));

		for (int i = 0; i < 6; i++)
		{
			vectorThrustDraw[i] = 0;
		}

		thrusterDraw = 0;
		for (int i = 0; i < fuelGrid.Thrusters.Count; i++)
		{
			IMyThrust thruster = fuelGrid.Thrusters[i].Block;
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
	if (Panels0.Count > 0)
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
		for (int i = 0; i < Panels0.Count; i++)
		{
			Panels0[i].WritePublicText(content);
		}
	}

	if (Panels1.Count > 0)
	{
		StringBuilder content = new StringBuilder();

		if (analyst.ThrusterMaxDraw != 0)
		{
			content.AppendFormat("Dir %    Hps    Maximum\n\n");
			for (int i = 5; i >= 0; i--)
			{
				if (analyst.ThrusterVectorMaxDraw[i] != 0)
				{
					content.AppendFormat("{0}   {1,-5}{2,-5}   {3,-5}\n", DIRECTIONS[5 - i], (int)(100 * analyst.ThrusterVectorDraw[i] / analyst.ThrusterVectorMaxDraw[i]), (int)analyst.ThrusterVectorDraw[i], (int)analyst.ThrusterVectorMaxDraw[i]);
				}
				else
				{
					content.AppendFormat("{0}        No data\n", DIRECTIONS[5 - i]);
				}
			}

			content.AppendFormat("         {0,-5}   {1,-5}\n", (int)analyst.ThrusterDraw, (int)analyst.ThrusterMaxDraw);
		}
		else
		{
			content.Append("No hydrogen thrusters detected!\n\nNote that you need to \nhave any type of cockpit on \nthe grid to gain access \nto thruster data!");
		}

		for (int i = 0; i < Panels1.Count; i++)
		{
			Panels1[i].WritePublicText(content);
		}
	}
}

public Program()
{
	Runtime.UpdateFrequency = UpdateFrequency.Update10 | UpdateFrequency.Update100;

	List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
	GridTerminalSystem.SearchBlocksOfName(PANEL_MARKERS[0], blocks);
	Panels0 = FilterBlockType<IMyTextPanel, IMyTerminalBlock>(blocks);
	blocks.Clear();
	GridTerminalSystem.SearchBlocksOfName(PANEL_MARKERS[1], blocks);
	Panels1 = FilterBlockType<IMyTextPanel, IMyTerminalBlock>(blocks);

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
#endregion