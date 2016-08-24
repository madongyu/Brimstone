﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Brimstone
{
	public class Empty : QueueAction
	{
		public override ActionResult Run(Game game, IEntity source, List<ActionResult> args) {
			return ActionResult.Empty;
		}
	}

	// TODO: Optimize this away during graph unravel
	public class FixedNumber : QueueAction
	{
		public int Num { get; set; }

		public override ActionResult Run(Game game, IEntity source, List<ActionResult> args) {
			return Num;
		}
	}

	public class FixedCard : QueueAction
	{
		public Card Card { get; set; }

		public override ActionResult Run(Game game, IEntity source, List<ActionResult> args) {
			return Card;
		}
	}

	public class LazyEntity : QueueAction
	{
		public int EntityId { get; set; }

		public override ActionResult Run(Game game, IEntity source, List<ActionResult> args) {
			return (Entity)game.Entities[EntityId];
		}
	}

	public enum SelectionSource
	{
		Game,
		Player,
		ActionSource
	}

	public class Selector : QueueAction
	{
		public SelectionSource SelectionSource { get; set; }

		public Func<IEntity, IEnumerable<IEntity>> Lambda { get; set; }

		public override ActionResult Run(Game game, IEntity source, List<ActionResult> args) {
			switch (SelectionSource) {
				case SelectionSource.Game:
					return Lambda(game).ToList();
				case SelectionSource.Player:
					if (source is Game)
						return Lambda(game.CurrentPlayer).ToList();
					else if (source is Player)
						return Lambda(source).ToList();
					else
						return Lambda(source.Controller).ToList();
				case SelectionSource.ActionSource:
					return Lambda(source).ToList();
				default:
					throw new NotImplementedException();
			}
		}
	}

	public class Func : QueueAction
	{
		public Action<IEntity> F { get; set; }

		public override ActionResult Run(Game game, IEntity source, List<ActionResult> args) {
			F(source);
			return ActionResult.None;
		}
	}

	public class RandomChoice : QueueAction
	{
		public const int ENTITIES = 0;

		public override ActionResult Run(Game game, IEntity source, List<ActionResult> args) {
			var entities = (List<IEntity>)args[ENTITIES];
			if (entities.Count == 0)
				return new List<IEntity>();
			var m = RNG.Between(0, entities.Count - 1);
			return (Entity)entities[m];
		}
	}

	public class RandomAmount : QueueAction
	{
		public const int MIN = 0;
		public const int MAX = 1;

		public override ActionResult Run(Game game, IEntity source, List<ActionResult> args) {
			return RNG.Between(args[MIN], args[MAX]);
		}
	}

	public class Repeat : QueueAction
	{
		public ActionGraph Actions { get; set; }

		public const int AMOUNT = 0;

		public override ActionResult Run(Game game, IEntity source, List<ActionResult> args) {
			for (int i = 0; i < args[AMOUNT]; i++)
				game.Queue(source, Actions);
			return ActionResult.None;
		}
	}

	public class BeginTurn : QueueAction
	{
		public override ActionResult Run(Game game, IEntity source, List<ActionResult> args) {
			game.CurrentPlayer = game.CurrentPlayer.Opponent;
			game.Step = Step.MAIN_ACTION;
			game.NextStep = Step.MAIN_END;

			game.Queue(game.CurrentPlayer, Actions.Draw(game.CurrentPlayer));

			return ActionResult.None;
		}
	}

	public class Give : QueueAction
	{
		public const int PLAYER = 0;
		public const int CARD = 1;

		public override ActionResult Run(Game game, IEntity source, List<ActionResult> args) {
			Player player = (Player)args[PLAYER];
			Card card = args[CARD];

			DebugLog.WriteLine("Giving {0} to {1}", card.Name, player.FriendlyName);

			if (card.Type == CardType.MINION)
				return new Minion(card) {Zone = player.Hand};
			if (card.Type == CardType.SPELL)
				return new Spell(card) {Zone = player.Hand};
			// TODO: Weapons

			return ActionResult.None;
		}
	}

	public class Draw : QueueAction
	{
		public const int PLAYER = 0;

		public override ActionResult Run(Game game, IEntity source, List<ActionResult> args) {
			Player player = (Player)args[PLAYER];

			if (!player.Deck.IsEmpty) {
				Entity entity = (Entity)player.Deck[1];

				DebugLog.WriteLine("{0} draws {1}", player.FriendlyName, entity.ShortDescription);

				entity.Zone = player.Hand;
				player.NumCardsDrawnThisTurn++;
				return entity;
			}

			DebugLog.WriteLine("{0} tries to draw but their deck is empty", player.FriendlyName);
			return ActionResult.None;
		}
	}

	public class Play : QueueAction
	{
		// TODO: Deal with targeting
		// TODO: Deal with spells moving to graveyard

		public const int ENTITY = 0;

		public override ActionResult Run(Game game, IEntity source, List<ActionResult> args) {
			Player player = (Player)source.Controller;
			Entity entity = args[ENTITY];

			entity.Zone = player.Board;

			DebugLog.WriteLine("{0} is playing {1}", player.FriendlyName, entity.ShortDescription);

			game.Queue(entity, entity.Card.Behaviour.Battlecry);
			return entity;
		}
	}

	public class Damage : QueueAction
	{
		public const int TARGETS = 0;
		public const int DAMAGE = 1;

		public override ActionResult Run(Game game, IEntity source, List<ActionResult> args) {
			if (args[TARGETS].HasResult)
				foreach (Character e in args[TARGETS]) {
					DebugLog.WriteLine("{0} is getting hit for {1} points of damage", e.ShortDescription, args[DAMAGE]);

					e.Damage += args[DAMAGE];
					e.CheckForDeath();

					// TODO: What if one of our targets gets killed?
				}
			return ActionResult.None;
		}
	}

	public class Death : QueueAction
	{
		public const int TARGETS = 0;

		public override ActionResult Run(Game game, IEntity source, List<ActionResult> args) {
			if (args[TARGETS].HasResult)
				foreach (var e in args[TARGETS]) {
					DebugLog.WriteLine("{0} dies", e.ShortDescription);

					if (e is Minion)
					{
						e.Zone = e.Controller.Graveyard;
						((Minion)e).Damage = 0;
						game.Queue(e, e.Card.Behaviour.Deathrattle);
					}
				}
			return ActionResult.None;
		}
	}

	public class CreateChoice : QueueAction
	{
		public const int PLAYER = 0;
		public const int ENTITIES = 1;
		public const int CHOICE_TYPE = 2;

		public override ActionResult Run(Game game, IEntity source, List<ActionResult> args) {
			var choice = new Choice(
				Controller: (IZoneOwner)(Entity)args[PLAYER],
				ChoiceType: (ChoiceType)(int)args[CHOICE_TYPE],
				Choices: args[ENTITIES]
			);
			((Player)args[PLAYER]).Choice = choice;

			// The mulligan is the only situation where:
			// 1. We are waiting for both players' input at the same time
			// 2. There will not be an action chaining on from the result
			// In all other cases, we must pause the queue until the user responds with a choice
			if (choice.ChoiceType != ChoiceType.MULLIGAN)
				game.ActionQueue.Paused = true;

			return ActionResult.None;
		}
	}
}
