﻿namespace PugnaSharpSDK
{
    using System;
    using System.Collections.Specialized;
    using System.Linq;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows.Input;
    using Ensage;
    using Ensage.Common.Enums;
    using Ensage.Common.Extensions;
    using Ensage.Common.Menu;
    using Ensage.Common.Threading;
    using Ensage.SDK.Extensions;
    using Ensage.SDK.Handlers;
    using Ensage.SDK.Helpers;
    using Ensage.SDK.Input;
    using Ensage.SDK.Inventory;
    using Ensage.SDK.Inventory.Metadata;
    using Ensage.SDK.Orbwalker;
    using Ensage.SDK.Orbwalker.Modes;
    using Ensage.SDK.Prediction;
    using Ensage.SDK.Prediction.Collision;
    using Ensage.SDK.TargetSelector;
    using Ensage.SDK.Abilities.Items;
    using Ensage.SDK.Abilities;
    using Ensage.SDK.Service;
    using log4net;
    using PlaySharp.Toolkit.Logging;
    using PlaySharp.Toolkit.Helper.Annotations;
    using SharpDX;
    using AbilityId = Ensage.AbilityId;
    using UnitExtensions = Ensage.SDK.Extensions.UnitExtensions;
    using System.Collections.Generic;

    [PublicAPI]
    public class PugnaSharp : KeyPressOrbwalkingModeAsync
    {
        private static readonly ILog Log = AssemblyLogs.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private readonly IServiceContext context;

        private IInventoryManager Inventory { get; }

        private IPrediction Prediction { get; }

        private ITargetSelectorManager TargetSelector { get; }

        public PugnaSharpConfig Config { get; }

        public PugnaSharp(
            Key key,
            PugnaSharpConfig config,
            IServiceContext context)
            : base(context, key)
        {
            this.Config = config;
            this.context = context;
            this.TargetSelector = TargetSelector;
            this.Inventory = Inventory;
            this.Prediction = Prediction;
        }


        [ItemBinding]
        private item_blink BlinkDagger { get; set; }

        [ItemBinding]
        private item_bloodthorn BloodThorn { get; set; }

        [ItemBinding]
        private item_hurricane_pike HurricanePike { get; set; }

        [ItemBinding]
        private item_shivas_guard ShivasGuard { get; set; }

        [ItemBinding]
        private item_mjollnir Mjollnir { get; set; }

        [ItemBinding]
        private item_veil_of_discord VeilofDiscord { get; set; }

        [ItemBinding]
        private item_rod_of_atos RodofAtos { get; set; }

        [ItemBinding]
        private item_sheepstick SheepStick { get; set; }

        [ItemBinding]
        private item_orchid Orchid { get; set; }

        [ItemBinding]
        public item_dagon Dagon1 { get; set; }

        [ItemBinding]
        public item_dagon_2 Dagon2 { get; set; }

        [ItemBinding]
        public item_dagon_3 Dagon3 { get; set; }

        [ItemBinding]
        public item_dagon_4 Dagon4 { get; set; }

        [ItemBinding]
        public item_dagon_5 Dagon5 { get; set; }

        public Dagon Dagon => Dagon1 ?? Dagon2 ?? Dagon3 ?? Dagon4 ?? (Dagon) Dagon5;


        private Ability Decrepify { get; set; }

        private Ability Ward { get; set; }

        private Ability Drain { get; set; }

        private Ability Blast { get; set; }

        protected bool IsHealing { get; set; }

        protected Unit HealTarget { get; set; }

        public override async Task ExecuteAsync(CancellationToken token)
        {
            var target = this.TargetSelector.Active.GetTargets().FirstOrDefault(x => !x.IsInvulnerable());

            var allTargets = this.TargetSelector.Active.GetTargets().FirstOrDefault();

            var silenced = UnitExtensions.IsSilenced(this.Owner);

            var sliderValue = this.Config.UseBlinkPrediction.Item.GetValue<Slider>().Value;

            var myHpThreshold = this.Config.SelfHPDrain.Item.GetValue<Slider>().Value;

            var postDrainHp = this.Config.PostDrainHP.Item.GetValue<Slider>().Value;

            var allyPostDrain = this.Config.HealAllyTo.Item.GetValue<Slider>().Value;

            var healThreshold = this.Config.DrainHP.Item.GetValue<Slider>().Value;

            var wardTars = this.Config.WardTargets.Item.GetValue<Slider>().Value;

            //warnings
            if (myHpThreshold < postDrainHp && this.Config.AbilityToggler.Value.IsEnabled(this.Drain.Name))
            {
                Log.Debug(
                    "Post drain hp is higher than your hp threshold to begin healing, please change this or the script won't work.");
                return;
            }

            if (healThreshold > allyPostDrain && this.Config.AbilityToggler.Value.IsEnabled(this.Drain.Name))
            {
                Log.Debug("Your ally's post heal threshold is lower than their heal threshold, please fix this.");
                return;
            }

            if (!silenced)
            {
                try
                {
                    var tempHealTarget =
                        EntityManager<Hero>.Entities.FirstOrDefault(
                            x =>
                                x.IsAlive && x.Team == this.Owner.Team && x != Owner && !x.IsIllusion
                                && ((float) x.Health / (float) x.MaximumHealth) * 100 < healThreshold
                                && !UnitExtensions.IsMagicImmune(x));

                    float myHealth = (float) Owner.Health / (float) Owner.MaximumHealth * 100;

                    if (tempHealTarget != null)
                    {
                        HealTarget = tempHealTarget;
                    }

                    if (HealTarget != null)
                    {
                        if (HealTarget != null && this.Config.AbilityToggler.Value.IsEnabled(this.Drain.Name)
                            && !UnitExtensions.IsChanneling(Owner) && myHealth >= myHpThreshold
                            && HealTarget.HealthPercent() * 100 < healThreshold)
                        {
                            this.Drain.UseAbility(HealTarget);
                            IsHealing = true;
                            await Await.Delay(GetAbilityDelay(HealTarget, Drain), token);
                        }

                        //Stop Healing; There is no hidden modifier/any way to check if we are healing a target.
                        if ((UnitExtensions.IsChanneling(Owner) && myHealth <= postDrainHp) && IsHealing)
                        {
                            Owner.Stop();
                            IsHealing = false;
                        }

                        if (HealTarget != null && IsHealing &&
                            (HealTarget.HealthPercent() >= ((float) allyPostDrain / 100)))
                        {
                            Owner.Stop();
                            IsHealing = false;
                        }

                        if (HealTarget == null && IsHealing)
                        {
                            Owner.Stop();
                            IsHealing = false;
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Debug($"{e}");
                }
            }


            if ((this.BlinkDagger != null) &&
                (this.BlinkDagger.Item.IsValid) &&
                target != null && Owner.Distance2D(target) <= 1200 + sliderValue && !(Owner.Distance2D(target) <= 400) &&
                this.BlinkDagger.Item.CanBeCasted(target) &&
                this.Config.ItemToggler.Value.IsEnabled(this.BlinkDagger.Item.Name)
                && !UnitExtensions.IsChanneling(Owner))
            {
                var l = (this.Owner.Distance2D(target) - sliderValue) / sliderValue;
                var posA = this.Owner.Position;
                var posB = target.Position;
                var x = (posA.X + (l * posB.X)) / (1 + l);
                var y = (posA.Y + (l * posB.Y)) / (1 + l);
                var position = new Vector3((int) x, (int) y, posA.Z);

                Log.Debug("Using BlinkDagger");
                this.BlinkDagger.UseAbility(position);
                await Await.Delay(this.GetItemDelay(position) + (int) Game.Ping, token);
            }


            if (!silenced && target != null)
            {
                var targets =
                    EntityManager<Hero>.Entities.Where(
                            x =>
                                x.IsValid && x.Team != this.Owner.Team && !x.IsIllusion &&
                                !UnitExtensions.IsMagicImmune(x) &&
                                x.Distance2D(this.Owner) <= Ward.GetAbilityData("radius"))
                        .ToList();

                if (targets.Count >= wardTars && this.Config.AbilityToggler.Value.IsEnabled(this.Ward.Name))
                {
                    Ward.UseAbility(Owner.NetworkPosition);
                    await Await.Delay(GetAbilityDelay(Owner, Ward), token);
                }

                // var thresholdTars = this.Config.WardTargets.Item.GetValue<Slider>();
                var manaDecrepify = Decrepify.GetManaCost(Decrepify.Level - 1);
                var manaBlast = Blast.GetManaCost(Blast.Level - 1);
                // var manaDrain = Drain.GetManaCost(Drain.Level - 1);
                try
                {
                    if (Decrepify.CanBeCasted() && target != null && Decrepify.CanHit(target)
                        && this.Config.AbilityToggler.Value.IsEnabled(this.Decrepify.Name)
                        && this.Owner.Mana >= manaBlast + manaDecrepify
                        && !UnitExtensions.IsChanneling(Owner)
                        && target.IsAlive)
                    {
                        this.Decrepify.UseAbility(target);
                        await Await.Delay(GetAbilityDelay(target, Decrepify), token);
                    }

                    if (this.Blast.CanBeCasted()
                        && this.Config.AbilityToggler.Value.IsEnabled(this.Blast.Name)
                        && (!this.Decrepify.CanBeCasted() || manaBlast > Owner.Mana - manaDecrepify)
                        && !UnitExtensions.IsChanneling(Owner)
                        && target != null && target.IsAlive)
                    {
                        var delay = Blast.GetAbilityData("delay") * 1000;
                        var blastTargets =
                            EntityManager<Hero>.Entities.OrderBy(x => x == allTargets).Where(
                                x =>
                                    x.IsValid && x.IsVisible && x.Team != Owner.Team && !x.IsIllusion &&
                                    !UnitExtensions.IsMagicImmune(x)).ToList();

                        if (blastTargets == null) return;
                        var input =
                            new PredictionInput(
                                Owner,
                                target,
                                delay,
                                float.MaxValue,
                                620,
                                400,
                                PredictionSkillshotType.SkillshotCircle,
                                true,
                                blastTargets)
                            {
                                CollisionTypes = CollisionTypes.None
                            };

                        var output = Prediction.GetPrediction(input);

                        if (output.HitChance >= HitChance.Medium)
                        {
                            this.Blast.UseAbility(output.CastPosition);
                            await Await.Delay(GetAbilityDelay(output.CastPosition, this.Blast), token);
                        }
                    }

                    if (this.Drain.CanBeCasted() &&
                        !this.Blast.CanBeCasted() && !this.Decrepify.CanBeCasted()
                        && this.Config.AbilityToggler.Value.IsEnabled(this.Drain.Name)
                        && !UnitExtensions.IsChanneling(Owner)
                        && target != null && target.IsAlive)
                    {
                        this.Drain.UseAbility(target);
                        await Await.Delay(GetAbilityDelay(target, Drain), token);
                    }
                }
                catch (Exception e)
                {
                    Log.Debug($"{e}");
                }
            }

            if (this.BloodThorn != null &&
                this.BloodThorn.Item.IsValid &&
                target != null && 
                this.BloodThorn.Item.CanBeCasted(target) &&
                this.Config.ItemToggler.Value.IsEnabled(this.BloodThorn.Item.Name))
            {
                Log.Debug("Using Bloodthorn");
                this.BloodThorn.UseAbility(target);
                await Await.Delay(this.GetItemDelay(target), token);
            }

            if ((this.SheepStick != null) &&
                (this.SheepStick.Item.IsValid) &&
                target != null &&
                this.SheepStick.Item.CanBeCasted(target) &&
                this.Config.ItemToggler.Value.IsEnabled("item_sheepstick"))
            {
                Log.Debug("Using Sheepstick");
                this.SheepStick.UseAbility(target);
                await Await.Delay(this.GetItemDelay(target), token);
            }

            if (this.Dagon != null &&
                this.Dagon.Item.IsValid &&
                target != null &&
                this.Dagon.Item.CanBeCasted(target) &&
                this.Config.ItemToggler.Value.IsEnabled(Dagon5.Item.Name))
            {
                Log.Debug("Using Dagon");
                this.Dagon.UseAbility(target);
                await Await.Delay(this.GetItemDelay(target), token);
            }

            if (this.Orchid != null &&
                this.Orchid.Item.IsValid &&
                target != null &&
                this.Orchid.Item.CanBeCasted(target) &&
                this.Config.ItemToggler.Value.IsEnabled("item_orchid"))
            {
                Log.Debug("Using Orchid");
                this.Orchid.UseAbility(target);
                await Await.Delay(this.GetItemDelay(target), token);
            }

            if (this.RodofAtos != null &&
                this.RodofAtos.Item.IsValid &&
                target != null &&
                this.RodofAtos.Item.CanBeCasted(target) &&
                this.Config.ItemToggler.Value.IsEnabled("item_rod_of_atos"))
            {
                Log.Debug("Using RodofAtos");
                this.RodofAtos.UseAbility(target);
                await Await.Delay(this.GetItemDelay(target), token);
            }

            if (this.VeilofDiscord != null &&
                this.VeilofDiscord.Item.IsValid &&
                target != null &&
                this.VeilofDiscord.Item.CanBeCasted() &&
                this.Config.ItemToggler.Value.IsEnabled("item_veil_of_discord"))
            {
                Log.Debug("Using VeilofDiscord");
                this.VeilofDiscord.UseAbility(target.Position);
                await Await.Delay(this.GetItemDelay(target), token);
            }

            if (this.HurricanePike != null &&
                this.HurricanePike.Item.IsValid &&
                target != null &&
                this.HurricanePike.Item.CanBeCasted() &&
                this.Config.ItemToggler.Value.IsEnabled("item_hurricane_pike"))
            {
                Log.Debug("Using HurricanePike");
                this.HurricanePike.UseAbility(target);
                await Await.Delay(this.GetItemDelay(target), token);
            }

            if (this.ShivasGuard != null &&
                this.ShivasGuard.Item.IsValid &&
                target != null &&
                this.ShivasGuard.Item.CanBeCasted() &&
                Owner.Distance2D(target) <= 900 &&
                this.Config.ItemToggler.Value.IsEnabled("item_shivas_guard"))
            {
                Log.Debug("Using Shiva's Guard");
                this.ShivasGuard.UseAbility();
                await Await.Delay((int) Game.Ping, token);
            }

            if (this.Mjollnir != null &&
                this.Mjollnir.Item.IsValid &&
                target != null &&
                this.Mjollnir.Item.CanBeCasted() &&
                this.Config.ItemToggler.Value.IsEnabled("item_mjollnir"))
            {
                Log.Debug("Using Mjollnir");
                this.Mjollnir.UseAbility(Owner);
                await Await.Delay(this.GetItemDelay(target), token);
            }

            if (this.Orbwalker.OrbwalkTo(target))
            {
                return;
            }

            await Await.Delay(50, token);
        }

        protected float GetSpellAmp()
        {
            // spell amp
            var me = Context.Owner as Hero;

            var spellAmp = (100.0f + me.TotalIntelligence / 16.0f) / 100.0f;

            var aether = Owner.GetItemById(ItemId.item_aether_lens);
            if (aether != null)
            {
                spellAmp += aether.AbilitySpecialData.First(x => x.Name == "spell_amp").Value / 100.0f;
            }

            var talent =
                Owner.Spellbook.Spells.FirstOrDefault(
                    x => x.Level > 0 && x.Name.StartsWith("special_bonus_spell_amplify_"));

            if (talent != null)
            {
                spellAmp += talent.AbilitySpecialData.First(x => x.Name == "value").Value / 100.0f;
            }

            return spellAmp;
        }

        public virtual async Task KillStealAsync()
        {
            var damageBlast = Blast.GetAbilityData("blast_damage");
            damageBlast *= GetSpellAmp();

            bool comboMana = Blast.GetManaCost(Blast.Level - 1) + Decrepify.GetManaCost(Decrepify.Level - 1) <
                             Owner.Mana;

            var decrepifyKillable =
                EntityManager<Hero>.Entities.FirstOrDefault(
                    x =>
                        x.IsAlive && x.Team != this.Owner.Team && !x.IsIllusion
                        && x.Health < damageBlast * (1 - x.MagicDamageResist) && Decrepify != null && Decrepify.IsValid
                        && Blast != null && Blast.IsValid
                        && Decrepify.CanBeCasted(x) && Blast.CanBeCasted()
                        && !UnitExtensions.IsMagicImmune(x) && comboMana);

            var blastKillable =
                EntityManager<Hero>.Entities.FirstOrDefault(
                    x =>
                        x.IsAlive && x.Team != this.Owner.Team && !x.IsIllusion
                        && x.Health < damageBlast * (1 - x.MagicDamageResist) && Blast != null && Blast.IsValid
                        && Blast.CanBeCasted() && !UnitExtensions.IsMagicImmune(x)
                        && Ensage.SDK.Extensions.EntityExtensions.Distance2D(Owner, x.NetworkPosition) <= 400);

            if (decrepifyKillable != null)
            {
                Decrepify.UseAbility(decrepifyKillable);
                await Await.Delay(GetAbilityDelay(decrepifyKillable, Decrepify));
                Blast.UseAbility(decrepifyKillable);
                await Await.Delay(GetAbilityDelay(decrepifyKillable, Decrepify));
            }

            if (blastKillable != null)
            {
                Blast.UseAbility(blastKillable.NetworkPosition);
                await Await.Delay(GetAbilityDelay(blastKillable, Blast));
            }
        }

        protected int GetAbilityDelay(Unit unit, Ability ability)
        {
            return (int) (((ability.FindCastPoint() + this.Owner.GetTurnTime(unit)) * 1000.0) + Game.Ping) + 50;
        }

        protected int GetAbilityDelay(Vector3 pos, Ability ability)
        {
            return (int)(((ability.FindCastPoint() + this.Owner.GetTurnTime(pos)) * 1000.0) + Game.Ping) + 50;
        }

        protected int GetItemDelay(Unit unit)
        {
            return (int) ((this.Owner.GetTurnTime(unit) * 1000.0) + Game.Ping) + 100;
        }

        protected int GetItemDelay(Vector3 pos)
        {
            return (int) ((this.Owner.GetTurnTime(pos) * 1000.0) + Game.Ping) + 100;
        }

        private void GameDispatcher_OnIngameUpdate(EventArgs args)
        {
            if (!this.Config.KillStealEnabled.Value)
            {
                return;
            }

            if (!Game.IsPaused && Owner.IsAlive && !UnitExtensions.IsChanneling(Owner))
            {
                Await.Block("MyKillstealer", KillStealAsync);
            }
        }

        protected override void OnActivate()
        {
            GameDispatcher.OnIngameUpdate += GameDispatcher_OnIngameUpdate;
            this.Decrepify = UnitExtensions.GetAbilityById(this.Owner, AbilityId.pugna_decrepify);
            this.Blast = UnitExtensions.GetAbilityById(this.Owner, AbilityId.pugna_nether_blast);
            this.Ward = UnitExtensions.GetAbilityById(this.Owner, AbilityId.pugna_nether_ward);
            this.Drain = UnitExtensions.GetAbilityById(this.Owner, AbilityId.pugna_life_drain);

            this.context.Inventory.Attach(this);

            base.OnActivate();
        }

        protected override void OnDeactivate()
        {
            GameDispatcher.OnIngameUpdate -= GameDispatcher_OnIngameUpdate;
            base.OnDeactivate();
            this.context.Inventory.Detach(this);
        }
    }
}