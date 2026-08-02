using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System.Linq;

// Grade 1 4x2x6 layout: 1 nurse WorkSpot (a normal IJobRoom job — assigned via the existing
// AssignmentUI, same as Garden/Water/Coal) plus 2 BedSpots (patients — NOT an IJobRoom concept, a
// patient isn't "working" here, it's assigned via the new PatientUI instead). Healing only ticks while
// a nurse is actively working, mirroring GardenRoom.ProduceCarrotsRoutine's activeProductionRoutines-
// gated shape — see HealPatientsRoutine.
public class HospitalRoom : RoomBase, IJobRoom
{
    [SpotNamePrefix("NursingSpot")]
    [SerializeField] private List<RoomSpot> nurseSpots;

    [SpotNamePrefix("BedSpot")]
    [SerializeField] private List<RoomSpot> bedSpots;

    [Header("Healing")]
    [SerializeField] private float hpGainPerSecondHospital = 2f;
    [SerializeField] private float healTickInterval = 1f;
    [Tooltip("0.25 = 1.25x heal rate while the active nurse's Type is in recommendedTypes — same default bonus every recommended-type room boost uses (see GardenRoom.typeMatchProductionBonus).")]
    [SerializeField] private float typeMatchHealBonus = 0.25f;
    [Tooltip("Pixie is the intended recommended type for this room (per design) — no art yet, but the type itself already exists in the BunnyType enum, so this is just data.")]
    [SerializeField] private List<BunnyType> recommendedTypes = new List<BunnyType> { BunnyType.Pixie };

    private NPCBunny activeNurse;
    private Coroutine healRoutine;
    private readonly Dictionary<RoomSpot, NPCBunny> bedOccupants = new Dictionary<RoomSpot, NPCBunny>();

    // Nurse idled because this room lost Power/Water while actively working — resumed by OnRoomRestored,
    // same shape as GardenRoom's idledByShutdown (just a single slot since there's only ever 1 nurse).
    private NPCBunny idledNurse;

    // ---------- IJobRoom (nurse — standard single-worker job assignment) ----------

    public RoomSpot RequestSpot(NPCBunny bunny)
    {
        foreach (RoomSpot spot in nurseSpots)
        {
            if (spot.TryClaim(bunny))
                return spot;
        }
        return null;
    }

    public bool HasAvailableSpot()
    {
        return nurseSpots != null && nurseSpots.Any(s => !s.IsOccupied);
    }

    public void ReleaseSpot(RoomSpot spot, NPCBunny bunny)
    {
        spot.Release(bunny);
        StopNursing(bunny);
    }

    public void NotifyBunnyLeavingToEat(NPCBunny bunny) => StopNursing(bunny);

    public void NotifyBunnyReadyToWork(NPCBunny bunny)
    {
        if (!IsOperational)
        {
            idledNurse = bunny;
            bunny.ForceIdleDueToRoomShutdown();
            return;
        }

        if (activeNurse == bunny) return;

        activeNurse = bunny;
        if (healRoutine == null)
            healRoutine = StartCoroutine(HealPatientsRoutine());
    }

    private void StopNursing(NPCBunny bunny)
    {
        if (activeNurse != bunny) return;

        activeNurse = null;
        if (healRoutine != null)
        {
            StopCoroutine(healRoutine);
            healRoutine = null;
        }
    }

    public void OnRoomShutdown()
    {
        if (activeNurse == null) return;

        idledNurse = activeNurse;
        NPCBunny nurse = activeNurse;
        StopNursing(nurse);
        nurse.ForceIdleDueToRoomShutdown();
    }

    public void OnRoomRestored()
    {
        if (idledNurse == null) return;

        idledNurse.ResumeWorkAfterRoomRestored();
        idledNurse = null;
    }

    // ---------- Beds (patients — see PatientUI, distinct from the IJobRoom nurse spot above) ----------

    public RoomSpot RequestBed(NPCBunny bunny)
    {
        foreach (RoomSpot spot in bedSpots)
        {
            if (spot.TryClaim(bunny))
            {
                bedOccupants[spot] = bunny;
                return spot;
            }
        }
        return null;
    }

    public bool HasAvailableBed()
    {
        return bedSpots != null && bedSpots.Any(s => !s.IsOccupied);
    }

    public void ReleaseBed(RoomSpot spot, NPCBunny bunny)
    {
        spot.Release(bunny);
        bedOccupants.Remove(spot);
    }

    // ---------- Healing tick ----------

    // Only runs while a nurse is actively Working here (started in NotifyBunnyReadyToWork, stopped in
    // StopNursing/OnRoomShutdown) — same "no worker, no effect" gating GardenRoom's production has,
    // just applied to bedded patients instead of a stockpile.
    private IEnumerator HealPatientsRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(healTickInterval);

            if (activeNurse == null || activeNurse.CurrentState != BunnyState.Working)
            {
                NPCBunny nurse = activeNurse;
                StopNursing(nurse);
                yield break;
            }

            bool typeMatch = recommendedTypes != null && recommendedTypes.Contains(activeNurse.Type);
            float rate = hpGainPerSecondHospital * GradeMultiplier * (typeMatch ? 1f + typeMatchHealBonus : 1f);
            int healAmount = Mathf.RoundToInt(rate * healTickInterval);
            if (healAmount <= 0) continue;

            // bedOccupants is populated the instant a bed is CLAIMED (RequestBed, called by PatientUI
            // before the patient has actually walked there — see AssignToHospitalBed's MoveAlongPath),
            // not once they arrive. Only heal patients who've actually reached their bed (CurrentState ==
            // Recovering) — a claimed-but-still-walking patient is still BunnyState.MovingToSpot.
            foreach (NPCBunny patient in bedOccupants.Values.ToList())
            {
                if (patient.CurrentState != BunnyState.Recovering) continue;
                patient.HealHP(healAmount);
            }
        }
    }
}
