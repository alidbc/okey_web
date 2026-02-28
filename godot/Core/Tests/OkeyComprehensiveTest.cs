using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using Godot;
using OkieRummyGodot.Core.Application;
using OkieRummyGodot.Core.Domain;

namespace OkieRummyGodot.Core.Tests;

public partial class OkeyComprehensiveTest : Node
{
	public override void _Ready()
	{
		RunTests();
	}

	public void RunTests()
	{
		GD.Print("--- Starting Okey Comprehensive Tests ---");

		TestRuleEngineSets();
		TestRuleEngineRuns();
		TestRuleEnginePairs();
		TestDeckLogic();
		TestMatchManagerFlow();
		TestScoringSystem();

		GD.Print("--- All Okey Tests Completed ---");
	}

	private void TestRuleEngineSets()
	{
		GD.Print("Testing RuleEngine Sets...");
		// Valid Set: Same value, different colors
		var validSet = new List<Tile> {
			new Tile("1", 5, TileColor.Red),
			new Tile("2", 5, TileColor.Black),
			new Tile("3", 5, TileColor.Blue)
		};
		
		var rack = CreateFullRack();
		SetRackRange(rack, 0, validSet);
		var (isValid, reason) = RuleEngine.ValidateHandGroups(rack.ToList());
		if (!isValid) GD.PrintErr($"Valid Set failed: {reason}");
		Debug.Assert(isValid, $"Valid Set should be valid. Reason: {reason}");

		// Invalid Set: Same value, duplicate color
		var invalidSet = new List<Tile> {
			new Tile("1", 5, TileColor.Red),
			new Tile("2", 5, TileColor.Red),
			new Tile("3", 5, TileColor.Blue)
		};
		SetRackRange(rack, 0, invalidSet);
		(isValid, reason) = RuleEngine.ValidateHandGroups(rack.ToList());
		Debug.Assert(!isValid, "Set with duplicate colors should be invalid.");
	}

	private void TestRuleEngineRuns()
	{
		GD.Print("Testing RuleEngine Runs...");
		// Valid Run: Same color, sequential
		var validRun = new List<Tile> {
			new Tile("1", 1, TileColor.Red),
			new Tile("2", 2, TileColor.Red),
			new Tile("3", 3, TileColor.Red)
		};
		var rack = CreateFullRack();
		SetRackRange(rack, 0, validRun);
		var (isValid, reason) = RuleEngine.ValidateHandGroups(rack.ToList());
		if (!isValid) GD.PrintErr($"Valid Run failed: {reason}");
		Debug.Assert(isValid, "Valid Run should be valid.");

		// Wrap around run: 12, 13, 1
		var wrapRun = new List<Tile> {
			new Tile("1", 12, TileColor.Red),
			new Tile("2", 13, TileColor.Red),
			new Tile("3", 1, TileColor.Red)
		};
		SetRackRange(rack, 0, wrapRun);
		(isValid, reason) = RuleEngine.ValidateHandGroups(rack.ToList());
		if (!isValid) GD.PrintErr($"Wrap-around Run failed: {reason}");
		Debug.Assert(isValid, "Wrap-around Run (12-13-1) should be valid.");
	}

	private void TestRuleEnginePairs()
	{
		GD.Print("Testing RuleEngine Pairs...");
		var rack = new List<Tile>();
		for (int i = 1; i <= 7; i++)
		{
			rack.Add(new Tile($"a{i}", i, (TileColor)(i % 4)));
			rack.Add(new Tile($"b{i}", i, (TileColor)(i % 4)));
		}
		var (isValid, reason) = RuleEngine.ValidatePairs(rack);
		if (!isValid) GD.PrintErr($"7 Pairs failed: {reason}");
		Debug.Assert(isValid, $"7 Pairs should be valid. Reason: {reason}");

		// Test with Wildcard
		var wildcardHand = new List<Tile>(rack);
		wildcardHand[13] = new Tile("w", 0, TileColor.Black) { IsWildcard = true };
		(isValid, reason) = RuleEngine.ValidatePairs(wildcardHand);
		if (!isValid) GD.PrintErr($"7 Pairs with Wildcard failed: {reason}");
		Debug.Assert(isValid, "7 Pairs with 1 Wildcard should be valid.");
	}

	private void TestDeckLogic()
	{
		GD.Print("Testing Deck Logic...");
		var deck = new Deck();
		// 104 standard tiles + 2 fake okeys = 106
		Debug.Assert(deck.RemainingCount == 106, $"Deck should have 106 tiles, has {deck.RemainingCount}");
		
		deck.ApplyOkeyRules(5, TileColor.Red);
		// Check if any tile is marked as wildcard
		bool foundWildcard = false;
		Tile fakeOkey = null;
		int count = deck.RemainingCount;
		for (int i = 0; i < count; i++)
		{
			var t = deck.Draw();
			if (t.IsWildcard) foundWildcard = true;
			if (t.IsFakeOkey) fakeOkey = t;
		}
		Debug.Assert(foundWildcard, "Wildcards should be assigned in Deck.");
		Debug.Assert(fakeOkey != null && fakeOkey.Value == 5 && fakeOkey.Color == TileColor.Red, "Fake Okey should take Okey value.");
	}

	private void TestMatchManagerFlow()
	{
		GD.Print("Testing Match Manager Flow...");
		var mm = new MatchManager();
		mm.AddPlayer(new Player("p1", "Player 1", ""));
		mm.AddPlayer(new Player("p2", "Player 2", ""));
		mm.StartGame();

		Debug.Assert(mm.Players[0].GetValidTiles().Count == 15, "First player should have 15 tiles.");
		Debug.Assert(mm.Players[1].GetValidTiles().Count == 14, "Other players should have 14 tiles.");

		// Test Indicator Match
		var player = mm.Players[0];
		player.Rack[0] = new Tile("ind", mm.IndicatorTile.Value, mm.IndicatorTile.Color);
		bool canShow = mm.CanShowIndicator("p1");
		if (!canShow) GD.PrintErr($"CanShowIndicator failed for p1. TurnID: {mm.TurnID}, Phase: {mm.CurrentPhase}, CurrentPlayerIndex: {mm.CurrentPlayerIndex}");
		
		Debug.Assert(canShow, "Player should be able to show indicator.");
		mm.ShowIndicator("p1");
		Debug.Assert(mm.Players[1].IndicatorPenaltyApplied, "Opponent should have indicator penalty.");
	}

	private void TestScoringSystem()
	{
		GD.Print("Testing Scoring System...");
		var mm = new MatchManager();
		var p1 = new Player("p1", "Winner", "");
		var p2 = new Player("p2", "Loser", "");
		mm.AddPlayer(p1);
		mm.AddPlayer(p2);
		mm.StartGame();

		// Mock a win
		mm.Status = GameStatus.Victory;
		mm.WinnerId = "p1";
		mm.IsPairWin = false;
		mm.IsOkeyFinish = false;

		var scores = mm.GetPlayerScores();
		var p2Score = scores.Find(s => s.PlayerId == "p2").Score;
		Debug.Assert(p2Score == 18, $"Standard win: Loser should have 18 points (20-2), has {p2Score}");

		mm.IsOkeyFinish = true;
		scores = mm.GetPlayerScores();
		p2Score = scores.Find(s => s.PlayerId == "p2").Score;
		Debug.Assert(p2Score == 16, $"Okey finish: Loser should have 16 points (20-4), has {p2Score}");

		p2.IndicatorPenaltyApplied = true;
		scores = mm.GetPlayerScores();
		p2Score = scores.Find(s => s.PlayerId == "p2").Score;
		Debug.Assert(p2Score == 15, $"Okey finish + Indicator: Loser should have 15 points (20-4-1), has {p2Score}");
	}

	// Helpers
	private Tile[] CreateFullRack()
	{
		var rack = new Tile[26];
		return rack;
	}

	private void SetRackRange(Tile[] rack, int start, List<Tile> tiles)
	{
		// Clear rack
		for (int i = 0; i < 26; i++) rack[i] = null;

		// Create 3 valid groups with GAPS between them
		// Group 1: 3 tiles (values 1-3, Black)
		for (int i = 0; i < 3; i++) rack[i] = new Tile($"g1_{i}", i + 1, TileColor.Black);
		
		// Group 2: 4 tiles (values 10, each color)
		rack[4] = new Tile("g2_0", 10, TileColor.Red);
		rack[5] = new Tile("g2_1", 10, TileColor.Black);
		rack[6] = new Tile("g2_2", 10, TileColor.Blue);
		rack[7] = new Tile("g2_3", 10, TileColor.Yellow);

		// Group 3: 4 tiles (values 1-4, Yellow)
		for (int i = 0; i < 4; i++) rack[9 + i] = new Tile($"g3_{i}", i + 1, TileColor.Yellow);

		if (tiles != null)
		{
			// Group 4: The custom tiles being tested (making sure total is 14)
			// Existing groups: 3 + 4 + 4 = 11. Custom needs to be 3.
			for (int i = 0; i < tiles.Count; i++) rack[14 + i] = tiles[i];
		}
	}
}
