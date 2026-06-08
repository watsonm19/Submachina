using System.Collections.Generic;
using UnityEngine;

namespace SynapticPro.GOAP
{
    /// <summary>
    /// GOAP Basic Behavior Pattern Template Collection
    /// </summary>
    public static class GOAPBehaviorTemplates
    {
        /// <summary>
        /// 1. Guard AI - Patrol and Intruder Response
        /// </summary>
        public static BehaviorTemplate GuardTemplate = new BehaviorTemplate
        {
            Name = "Guard AI",
            Description = "Patrols designated area and responds to threats",
            Goals = new List<Goal>
            {
                new Goal { Name = "MaintainSecurity", Priority = 100 },
                new Goal { Name = "PatrolArea", Priority = 80 },
                new Goal { Name = "InvestigateDisturbance", Priority = 90 },
                new Goal { Name = "EliminateThreat", Priority = 110 }
            },
            Actions = new List<GOAPAction>
            {
                new GOAPAction 
                { 
                    Name = "PatrolWaypoints",
                    Cost = 1f,
                    Preconditions = new[] { "on_duty", "no_threats" },
                    Effects = new[] { "area_patrolled" }
                },
                new GOAPAction
                {
                    Name = "InvestigateSound",
                    Cost = 1.5f,
                    Preconditions = new[] { "sound_detected" },
                    Effects = new[] { "sound_investigated" }
                },
                new GOAPAction
                {
                    Name = "RaiseAlarm",
                    Cost = 0.5f,
                    Preconditions = new[] { "threat_confirmed" },
                    Effects = new[] { "alarm_raised", "backup_requested" }
                },
                new GOAPAction
                {
                    Name = "EngageIntruder",
                    Cost = 2f,
                    Preconditions = new[] { "has_weapon", "intruder_in_range" },
                    Effects = new[] { "intruder_neutralized" }
                },
                new GOAPAction
                {
                    Name = "CallBackup",
                    Cost = 0.3f,
                    Preconditions = new[] { "radio_available", "threat_detected" },
                    Effects = new[] { "backup_called" }
                }
            },
            Sensors = new[] { "sound_detector", "motion_sensor", "threat_evaluator", "radio_checker" },
            InitialWorldState = new Dictionary<string, object>
            {
                ["on_duty"] = true,
                ["has_weapon"] = true,
                ["radio_available"] = true,
                ["patrol_route"] = "defined"
            }
        };

        /// <summary>
        /// 2. Collector Worker AI - Resource Gathering and Transportation
        /// </summary>
        public static BehaviorTemplate CollectorTemplate = new BehaviorTemplate
        {
            Name = "Collector AI",
            Description = "Gathers resources and delivers them to storage",
            Goals = new List<Goal>
            {
                new Goal { Name = "MaximizeResourceCollection", Priority = 100 },
                new Goal { Name = "MaintainEfficiency", Priority = 70 },
                new Goal { Name = "AvoidDanger", Priority = 90 }
            },
            Actions = new List<GOAPAction>
            {
                new GOAPAction
                {
                    Name = "LocateResource",
                    Cost = 1f,
                    Preconditions = new[] { "inventory_not_full", "energy_available" },
                    Effects = new[] { "resource_located" }
                },
                new GOAPAction
                {
                    Name = "MoveToResource",
                    Cost = 1.5f,
                    Preconditions = new[] { "resource_located" },
                    Effects = new[] { "at_resource" }
                },
                new GOAPAction
                {
                    Name = "GatherResource",
                    Cost = 2f,
                    Preconditions = new[] { "at_resource", "has_tool" },
                    Effects = new[] { "resource_collected", "inventory_increased" }
                },
                new GOAPAction
                {
                    Name = "ReturnToBase",
                    Cost = 1.5f,
                    Preconditions = new[] { "inventory_full" },
                    Effects = new[] { "at_base" }
                },
                new GOAPAction
                {
                    Name = "DepositResource",
                    Cost = 0.5f,
                    Preconditions = new[] { "at_base", "has_resource" },
                    Effects = new[] { "resource_deposited", "inventory_empty" }
                },
                new GOAPAction
                {
                    Name = "Rest",
                    Cost = 1f,
                    Preconditions = new[] { "energy_low" },
                    Effects = new[] { "energy_restored" }
                },
                new GOAPAction
                {
                    Name = "FleeFromDanger",
                    Cost = 0.1f,
                    Preconditions = new[] { "danger_detected" },
                    Effects = new[] { "safe_distance" }
                }
            },
            Sensors = new[] { "resource_scanner", "inventory_monitor", "energy_tracker", "danger_detector" },
            InitialWorldState = new Dictionary<string, object>
            {
                ["has_tool"] = true,
                ["inventory_capacity"] = 10,
                ["energy"] = 100,
                ["base_location"] = "known"
            }
        };

        /// <summary>
        /// 3. Combat Soldier AI - Tactical Combat and Cooperative Behavior
        /// </summary>
        public static BehaviorTemplate SoldierTemplate = new BehaviorTemplate
        {
            Name = "Combat Soldier AI",
            Description = "Engages enemies tactically with squad coordination",
            Goals = new List<Goal>
            {
                new Goal { Name = "EliminateEnemies", Priority = 100 },
                new Goal { Name = "SurviveCombat", Priority = 95 },
                new Goal { Name = "SupportSquad", Priority = 85 },
                new Goal { Name = "SecureObjective", Priority = 90 }
            },
            Actions = new List<GOAPAction>
            {
                new GOAPAction
                {
                    Name = "TakeCover",
                    Cost = 0.5f,
                    Preconditions = new[] { "under_fire", "cover_available" },
                    Effects = new[] { "in_cover", "damage_reduced" }
                },
                new GOAPAction
                {
                    Name = "AimAndShoot",
                    Cost = 1f,
                    Preconditions = new[] { "enemy_visible", "has_ammo", "weapon_ready" },
                    Effects = new[] { "damage_dealt", "ammo_consumed" }
                },
                new GOAPAction
                {
                    Name = "SuppressiveFire",
                    Cost = 2f,
                    Preconditions = new[] { "enemy_position_known", "ammo_sufficient" },
                    Effects = new[] { "enemy_suppressed" }
                },
                new GOAPAction
                {
                    Name = "Reload",
                    Cost = 1.5f,
                    Preconditions = new[] { "ammo_low", "has_magazine" },
                    Effects = new[] { "weapon_reloaded" }
                },
                new GOAPAction
                {
                    Name = "ThrowGrenade",
                    Cost = 2f,
                    Preconditions = new[] { "has_grenade", "enemy_clustered" },
                    Effects = new[] { "area_cleared" }
                },
                new GOAPAction
                {
                    Name = "RequestMedic",
                    Cost = 0.3f,
                    Preconditions = new[] { "health_critical", "medic_available" },
                    Effects = new[] { "healing_requested" }
                },
                new GOAPAction
                {
                    Name = "FlankEnemy",
                    Cost = 2.5f,
                    Preconditions = new[] { "flank_route_available", "squad_covering" },
                    Effects = new[] { "enemy_flanked" }
                },
                new GOAPAction
                {
                    Name = "CoverAlly",
                    Cost = 1f,
                    Preconditions = new[] { "ally_needs_cover", "position_good" },
                    Effects = new[] { "ally_covered" }
                }
            },
            Sensors = new[] { "enemy_tracker", "squad_coordinator", "ammo_counter", "health_monitor", "cover_finder" },
            InitialWorldState = new Dictionary<string, object>
            {
                ["weapon"] = "assault_rifle",
                ["ammo"] = 120,
                ["magazines"] = 4,
                ["grenades"] = 2,
                ["health"] = 100,
                ["squad_size"] = 4
            }
        };

        /// <summary>
        /// 4. Wildlife AI - Survival Instincts and Territorial Behavior
        /// </summary>
        public static BehaviorTemplate WildlifeTemplate = new BehaviorTemplate
        {
            Name = "Wildlife AI",
            Description = "Survives through hunting, territorial behavior, and avoiding predators",
            Goals = new List<Goal>
            {
                new Goal { Name = "Survive", Priority = 100 },
                new Goal { Name = "FindFood", Priority = 90 },
                new Goal { Name = "DefendTerritory", Priority = 70 },
                new Goal { Name = "Reproduce", Priority = 60 }
            },
            Actions = new List<GOAPAction>
            {
                new GOAPAction
                {
                    Name = "HuntPrey",
                    Cost = 2f,
                    Preconditions = new[] { "hungry", "prey_detected", "energy_sufficient" },
                    Effects = new[] { "food_obtained" }
                },
                new GOAPAction
                {
                    Name = "Graze",
                    Cost = 1f,
                    Preconditions = new[] { "vegetation_available", "safe_area" },
                    Effects = new[] { "hunger_reduced" }
                },
                new GOAPAction
                {
                    Name = "DrinkWater",
                    Cost = 0.5f,
                    Preconditions = new[] { "thirsty", "water_source_nearby" },
                    Effects = new[] { "thirst_quenched" }
                },
                new GOAPAction
                {
                    Name = "FleeFromPredator",
                    Cost = 0.1f,
                    Preconditions = new[] { "predator_detected" },
                    Effects = new[] { "safe_from_predator" }
                },
                new GOAPAction
                {
                    Name = "DefendTerritory",
                    Cost = 1.5f,
                    Preconditions = new[] { "intruder_in_territory", "strong_enough" },
                    Effects = new[] { "territory_secured" }
                },
                new GOAPAction
                {
                    Name = "MarkTerritory",
                    Cost = 0.5f,
                    Preconditions = new[] { "territory_unmarked" },
                    Effects = new[] { "territory_marked" }
                },
                new GOAPAction
                {
                    Name = "RestInDen",
                    Cost = 1f,
                    Preconditions = new[] { "tired", "den_available" },
                    Effects = new[] { "energy_restored" }
                },
                new GOAPAction
                {
                    Name = "CallMate",
                    Cost = 1f,
                    Preconditions = new[] { "mating_season", "no_mate" },
                    Effects = new[] { "mate_attracted" }
                }
            },
            Sensors = new[] { "smell_sensor", "hearing_sensor", "hunger_monitor", "thirst_monitor", "threat_detector", "territory_scanner" },
            InitialWorldState = new Dictionary<string, object>
            {
                ["species"] = "wolf",
                ["hunger"] = 50,
                ["thirst"] = 30,
                ["energy"] = 80,
                ["health"] = 100,
                ["territory_size"] = 100
            }
        };

        /// <summary>
        /// 5. Merchant NPC AI - Trading and Economic Activities
        /// </summary>
        public static BehaviorTemplate MerchantTemplate = new BehaviorTemplate
        {
            Name = "Merchant NPC AI",
            Description = "Trades with players, manages inventory, and maximizes profit",
            Goals = new List<Goal>
            {
                new Goal { Name = "MaximizeProfit", Priority = 100 },
                new Goal { Name = "MaintainInventory", Priority = 80 },
                new Goal { Name = "BuildReputation", Priority = 70 },
                new Goal { Name = "StaySafe", Priority = 90 }
            },
            Actions = new List<GOAPAction>
            {
                new GOAPAction
                {
                    Name = "GreetCustomer",
                    Cost = 0.2f,
                    Preconditions = new[] { "customer_nearby", "shop_open" },
                    Effects = new[] { "customer_engaged" }
                },
                new GOAPAction
                {
                    Name = "NegotiatePrice",
                    Cost = 1f,
                    Preconditions = new[] { "customer_interested", "item_available" },
                    Effects = new[] { "price_negotiated" }
                },
                new GOAPAction
                {
                    Name = "CompleteSale",
                    Cost = 0.5f,
                    Preconditions = new[] { "price_agreed", "item_in_stock" },
                    Effects = new[] { "sale_completed", "gold_increased" }
                },
                new GOAPAction
                {
                    Name = "RestockInventory",
                    Cost = 3f,
                    Preconditions = new[] { "stock_low", "gold_sufficient" },
                    Effects = new[] { "inventory_restocked" }
                },
                new GOAPAction
                {
                    Name = "HireGuard",
                    Cost = 2f,
                    Preconditions = new[] { "threat_level_high", "gold_available" },
                    Effects = new[] { "shop_protected" }
                },
                new GOAPAction
                {
                    Name = "AdvertiseWares",
                    Cost = 1f,
                    Preconditions = new[] { "customers_few" },
                    Effects = new[] { "customers_attracted" }
                },
                new GOAPAction
                {
                    Name = "CloseShop",
                    Cost = 0.5f,
                    Preconditions = new[] { "danger_imminent" },
                    Effects = new[] { "shop_secured" }
                },
                new GOAPAction
                {
                    Name = "OfferDiscount",
                    Cost = 1.5f,
                    Preconditions = new[] { "inventory_excess", "customer_hesitant" },
                    Effects = new[] { "sale_likely" }
                }
            },
            Sensors = new[] { "customer_detector", "inventory_tracker", "market_analyzer", "threat_assessor", "reputation_monitor" },
            InitialWorldState = new Dictionary<string, object>
            {
                ["gold"] = 1000,
                ["shop_location"] = "market_square",
                ["inventory_slots"] = 20,
                ["reputation"] = 50,
                ["shop_open"] = true
            }
        };
    }

    /// <summary>
    /// Behavior Template Structure
    /// </summary>
    public class BehaviorTemplate
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public List<Goal> Goals { get; set; }
        public List<GOAPAction> Actions { get; set; }
        public string[] Sensors { get; set; }
        public Dictionary<string, object> InitialWorldState { get; set; }
    }

    /// <summary>
    /// GOAP Goal Template Data (for serialization/templates)
    /// Use GOAPGoal class for runtime goals
    /// </summary>
    public class Goal
    {
        public string Name { get; set; }
        public float Priority { get; set; }

        /// <summary>
        /// Convert to runtime GOAPGoal
        /// </summary>
        public GOAPGoal ToRuntimeGoal()
        {
            return new GOAPGoal(Name, (int)Priority);
        }
    }

    /// <summary>
    /// GOAP Action Template Data (for serialization/templates)
    /// Use GOAPActionBase or GOAPDynamicAction for runtime actions
    /// </summary>
    public class GOAPAction
    {
        public string Name { get; set; }
        public float Cost { get; set; }
        public string[] Preconditions { get; set; }
        public string[] Effects { get; set; }

        /// <summary>
        /// Create runtime action from this template
        /// </summary>
        public GOAPDynamicAction CreateRuntimeAction(GameObject parent)
        {
            return GOAPActionFactory.CreateFromBehaviorData(
                parent,
                Name,
                Cost,
                Preconditions,
                Effects
            );
        }
    }

    /// <summary>
    /// Extension methods for applying templates to agents
    /// </summary>
    public static class BehaviorTemplateExtensions
    {
        /// <summary>
        /// Apply a behavior template to a GOAPAgent
        /// </summary>
        public static void ApplyTemplate(this GOAPAgent agent, BehaviorTemplate template)
        {
            if (agent == null || template == null) return;

            // Apply goals
            if (template.Goals != null)
            {
                foreach (var goalData in template.Goals)
                {
                    var goal = goalData.ToRuntimeGoal();
                    goal.IsActive = true;
                    agent.AddGoal(goal);
                }
            }

            // Apply actions
            if (template.Actions != null)
            {
                foreach (var actionData in template.Actions)
                {
                    var action = actionData.CreateRuntimeAction(agent.gameObject);
                    agent.AddAction(action);
                }
            }

            // Apply initial world state
            if (template.InitialWorldState != null)
            {
                foreach (var kvp in template.InitialWorldState)
                {
                    agent.SetWorldState(kvp.Key, kvp.Value);
                }
            }

            Debug.Log($"[GOAP] Applied template '{template.Name}' to agent '{agent.name}'");
        }
    }
}