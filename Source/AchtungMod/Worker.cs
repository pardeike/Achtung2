using Verse;
using RimWorld;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Verse.AI;

namespace AchtungMod
{
    public static class Worker
    {
        public static bool isDragging = false;
        public static Vector3 dragStart;
        public static IntVec3[] lastCells;

        // make sure we deal with simple colonists that can take orders and can be drafted
        //
        public static List<Pawn> selectedPawns()
        {
            List<Pawn> allPawns = Find.Selector.SelectedObjects.OfType<Pawn>().ToList();
            return allPawns.FindAll(pawn => pawn.drafter != null && pawn.drafter.CanTakeOrderedJob());
        }

        // OrderToCell drafts pawns if they are not and orders them to a specific location
        // if you don't want drafting you can disable it temporarily by holding shift in
        // which case the colonists run to the clicked location just to give it up as soom
        // as they reach it
        //
        public static void OrderToCell(Pawn pawn, IntVec3 cell)
        {
            bool shiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (!shiftPressed)
            {
                if (pawn.drafter == null)
                {
                    pawn.drafter = new Pawn_DraftController(pawn);
                }
                if (pawn.drafter.Drafted == false)
                {
                    pawn.drafter.Drafted = true;
                }
            }

            Job job = new Job(JobDefOf.Goto, cell);
            job.playerForced = true;
            pawn.drafter.TakeOrderedJob(job);
        }

        // main routine: handle mouse events and command your colonists!
        //
        public static void RightClickHandler(EventType type, Vector3 where)
        {
            if (type == EventType.MouseDown)
            {
                List<Pawn> pawns = selectedPawns();
                bool altPressed = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                if (pawns.Count > 1 || (pawns.Count == 1 && altPressed))
                {
                    lastCells = pawns.Select(p => IntVec3.Invalid).ToArray<IntVec3>();
                    dragStart = where;

                    isDragging = true;
                }
            }

            if (type == EventType.MouseUp && isDragging == true)
            {
                isDragging = false;
            }

            if ((type == EventType.MouseDown || type == EventType.MouseDrag) && isDragging == true)
            {
                List<Pawn> pawns = selectedPawns();
                int pawnCount = pawns.Count;
                if (pawnCount > 0)
                {
                    Vector3 dragEnd = where;
                    Vector3 line = dragEnd - dragStart;
                    Vector3 delta = pawnCount > 1 ? line / (float)(pawnCount - 1) : Vector3.zero;
                    Vector3 linePosition = pawnCount == 1 ? dragEnd : dragStart;

                    int i = 0;
                    foreach (Pawn pawn in pawns)
                    {
                        IntVec3 lastCell = lastCells[i];

                        IntVec3 optimalCell = linePosition.ToIntVec3();
                        IntVec3 cell = Pawn_DraftController.BestGotoDestNear(optimalCell, pawn);
                        MoteThrower.ThrowStatic(cell, ThingDefOf.Mote_FeedbackGoto);

                        if (lastCell.IsValid == false || cell != lastCell)
                        {
                            lastCells[i] = cell;
                            OrderToCell(pawn, cell);
                        }

                        linePosition += delta;
                        i++;
                    }
                }
            }
        }
    }
}
