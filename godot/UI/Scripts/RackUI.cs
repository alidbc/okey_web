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

	[Export] public float TopRowShift = 15.0f; 
	[Export] public float BottomRowShift = -12.0f;

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
		StyleBoxFlat topStyle = new StyleBoxFlat { BgColor = new Color(0,0,0,0), ContentMarginTop = TopRowShift };
		StyleBoxFlat bottomStyle = new StyleBoxFlat { BgColor = new Color(0,0,0,0), ContentMarginTop = BottomRowShift };
		
		for (int i = 0; i < 26; i++)
		{
			var slot = new RackSlotUI();
			slot.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			slot.SizeFlagsVertical = SizeFlags.ExpandFill;
			
			// Apply the appropriate shift style per row
			slot.AddThemeStyleboxOverride("panel", i < 13 ? topStyle : bottomStyle);
			
			slot.SlotIndex = i;
			slot.Connect("TileMoved", new Callable(this, nameof(OnTileMoved)));
			slot.Connect("DrawToSlot", new Callable(this, nameof(OnDrawToSlot)));
			
			if (i < 13) _topRow.AddChild(slot);
			else _bottomRow.AddChild(slot);
			
			var tileUI = TileScene.Instantiate<TileUI>();
			tileUI.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			tileUI.SizeFlagsVertical = SizeFlags.ExpandFill;
			tileUI.CustomMinimumSize = new Vector2(0, 0);
			
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

	private void OnTileMoved(int fromIndex, int toIndex)
	{
		if (_playerData != null)
		{
			_playerData.MoveTile(fromIndex, toIndex);
			RefreshVisuals();
			EmitSignal(nameof(TileMoved), fromIndex, toIndex);
		}
	}

	private void OnDrawToSlot(bool fromDiscard, int toIndex)
	{
		EmitSignal(nameof(DrawToSlot), fromDiscard, toIndex);
	}

	public Control GetSlotNode(int index)
	{
		if (index >= 0 && index < _slots.Count)
		{
			return _slots[index].GetParent() as Control;
		}
		return null;
	}

	public TileUI GetTileUI(int index)
	{
		if (index >= 0 && index < _slots.Count)
		{
			return _slots[index];
		}
		return null;
	}

	[Signal]
	public delegate void TileMovedEventHandler(int fromIndex, int toIndex);

	[Signal]
	public delegate void DrawToSlotEventHandler(bool fromDiscard, int toIndex);
}
