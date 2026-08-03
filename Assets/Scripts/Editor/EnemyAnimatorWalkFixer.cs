using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

// One-off fix for Enemy_Slime_Toxic.controller: EnemyInstance's movement code (offscreen siege approach,
// post-breach interior walk-in, melee flank-approach) never had any Animator parameter to drive a Walking
// state — the controller's own "Enemy_Slime_Walking" state existed with zero transitions in or out of it,
// so every movement leg just sat on whatever state Idle/Appearing left it in and slid across the floor
// instead of playing the authored Walking clip. Uses the AnimatorController API (rather than hand-editing
// the controller's YAML) for the same reason CombatBalanceConfigGenerator avoids hand-authoring
// ScriptableObject YAML — Unity's own serializer stays authoritative for fileIDs/references.
//
// Adds an "IsMoving" bool parameter (matching NPCBunny.isMovingParam's own naming convention, and
// EnemyInstance.isMovingParam's default) plus a direct Idle<->Walking transition pair with no exit time —
// snaps immediately whenever EnemyInstance.SetMoving flips the bool, matching how a pure locomotion loop
// should behave. Safe to re-run: skips whatever already exists instead of duplicating it.
public static class EnemyAnimatorWalkFixer
{
    private const string ControllerPath = "Assets/Art/Enemies/Slimes/Animations/Enemy_Slime_Toxic.controller";
    private const string IdleStateName = "Enemy_Slime_Idle";
    private const string WalkingStateName = "Enemy_Slime_Walking";
    private const string MovingParam = "IsMoving";

    [MenuItem("Burrowscape/Fix Slime Walking Animation")]
    public static void Fix()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogWarning($"EnemyAnimatorWalkFixer: no AnimatorController found at {ControllerPath}.");
            return;
        }

        if (Array.FindIndex(controller.parameters, p => p.name == MovingParam) < 0)
            controller.AddParameter(MovingParam, AnimatorControllerParameterType.Bool);

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        AnimatorState idleState = FindState(stateMachine, IdleStateName);
        AnimatorState walkingState = FindState(stateMachine, WalkingStateName);

        if (idleState == null || walkingState == null)
        {
            Debug.LogWarning($"EnemyAnimatorWalkFixer: couldn't find '{IdleStateName}' and/or '{WalkingStateName}' states in {ControllerPath}.");
            return;
        }

        if (!Array.Exists(idleState.transitions, t => t.destinationState == walkingState))
        {
            AnimatorStateTransition toWalking = idleState.AddTransition(walkingState);
            toWalking.hasExitTime = false;
            toWalking.duration = 0.1f;
            toWalking.AddCondition(AnimatorConditionMode.If, 0, MovingParam);
        }

        if (!Array.Exists(walkingState.transitions, t => t.destinationState == idleState))
        {
            AnimatorStateTransition toIdle = walkingState.AddTransition(idleState);
            toIdle.hasExitTime = false;
            toIdle.duration = 0.1f;
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0, MovingParam);
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log($"EnemyAnimatorWalkFixer: '{MovingParam}' parameter + {IdleStateName}<->{WalkingStateName} transitions are wired on {ControllerPath}.");
    }

    private static AnimatorState FindState(AnimatorStateMachine stateMachine, string stateName)
    {
        foreach (ChildAnimatorState child in stateMachine.states)
        {
            if (child.state.name == stateName) return child.state;
        }
        return null;
    }
}
