using Godot;
using OkieRummyGodot.Core.Domain;
using System;
using System.Collections.Generic;

namespace OkieRummyGodot.UI.Scripts;

public partial class RackUI : Control
{
	[Export] public PackedScene TileScene;
	
	private Player _playerData;
	private List<TileUI> _slots = new();
	
	// We get a reference to the actual HBoxContainer in the scene
			private HBoxContainer _topRow;
	private HBoxContainer _bottomRow;
	private TextureRect _rackBackgroundImage;

	public override void _Ready()
	{
		_topRow = GetNode<HBoxContainer>("TopRow");
		_bottomRow = GetNode<HBoxContainer>("BottomRow");
		_rackBackgroundImage = GetNode<TextureRect>("Background_RackImage");

		try 
		{
			Texture2D tex = GD.Load<Texture2D>("res://Assets/rack.png");
			if (_rackBackgroundImage != null) _rackBackgroundImage.Texture = tex;
		}
		catch (Exception)
		{
			GD.PrintErr("Could not load res://Assets/rack.png");
		}
	}

	public void Initialize(Player player)
	{
		_playerData = player;
		if (_topRow != null && _bottomRow != null) CreateSlots();
	}

	private void CreateSlots()
	{
		// Clear children
		foreach (Node child in _topRow.GetChildren())
		{
			_topRow.RemoveChild(child);
			child.QueueFree();
		}
		foreach (Node child in _bottomRow.GetChildren())
		{
			_bottomRow.RemoveChild(child);
			child.QueueFree();
		}
		
		_slots.Clear();

		// Okey has 26 rack slots per player (2 rows of 13)
		// Create an empty style box ONCE to share
		StyleBoxEmpty emptyStyle = new StyleBoxEmpty();
		
		for (int i = 0; i < 26; i++)
		{
			var slot = new PanelContainer();
			// Fluid slot scaling (handled by HBoxContainer expand tags), but enforce ratio
			slot.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			slot.SizeFlagsVertical = SizeFlags.ExpandFill;
			
			// Invisible background for drops
			slot.AddThemeStyleboxOverride("panel", emptyStyle);
			
			// Allow slots to receive drops
			slot.SetScript(ResourceLoader.Load("res://UI/Scripts/RackSlotUI.cs"));
			slot.Set("SlotIndex", i);
			
			if (i < 13)
			{
				_topRow.AddChild(slot);
			}
			else
			{
				_bottomRow.AddChild(slot);
			}
			
			var tileUI = TileScene.Instantiate<TileUI>();
			
			// Tell the tile to expand fully inside the slot (like React fluid=true)
			tileUI.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			tileUI.SizeFlagsVertical = SizeFlags.ExpandFill;
			tileUI.CustomMinimumSize = new Vector2(0, 0); // Allow shrinking
			
			slot.AddChild(tileUI);
			_slots.Add(tileUI);
		}
		
		RefreshVisuals();
	}

	public void RefreshVisuals()
	{
		if (_playerData == null) return;

		for (int i = 0; i < 26; i++)
		{
			Tile tile = _playerData.Rack[i];
			_slots[i].SetTileData(tile);
		}
	}
}

// A simple script to attach to each rack slot to handle dropping
public partial class RackSlotUI : PanelContainer
{
	public int SlotIndex { get; set; }
	
	// Check if what is dragged is a TileUI
	public override bool _CanDropData(Vector2 atPosition, Variant data)
	{
		return data.As<Node>() is TileUI;
	}

	public override void _DropData(Vector2 atPosition, Variant data)
	{
		var draggedTileUI = data.As<TileUI>();
		
		// Find if it came from another slot
		var fromSlot = draggedTileUI.GetParent() as RackSlotUI;
		if (fromSlot != null)
		{
			// Execute move logic (signal to Main.cs or directly via MatchManager if passed down)
			EmitSignal(nameof(TileMoved), fromSlot.SlotIndex, SlotIndex);
		}
	}

	[Signal]
	public delegate void TileMovedEventHandler(int fromIndex, int toIndex);
}
