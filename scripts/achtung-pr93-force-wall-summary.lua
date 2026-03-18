local saveName = params.saveName or "pr93"
local pawnName = params.pawnName or "Sienna"
local targetX = params.targetX or 86
local targetZ = params.targetZ or 125
local rectX = params.rectX or 12
local rectZ = params.rectZ or 125
local rectWidth = params.rectWidth or 75
local rectHeight = params.rectHeight or 1
local completionX = params.completionX or rectX
local completionZ = params.completionZ or rectZ
local speed = params.speed or "Ultrafast"
local labelContains = params.labelContains or "constructing wooden wall"

local load = rb.call("rimworld/load_game", {
  saveName = saveName
})
rb.assert(load.result.success == true, "Loading the requested save failed.")

local ready = rb.call("rimbridge/wait_for_game_loaded", {
  timeoutMs = 120000,
  pollIntervalMs = 100,
  waitForScreenFade = true,
  pauseIfNeeded = true
})
rb.assert(ready.result.success == true, "Game did not become automation-ready.")

rb.call("rimworld/select_pawn", {
  pawnName = pawnName,
  append = false
})

local logging = rb.call("rimworld/set_colonist_job_logging", {
  pawnName = pawnName,
  enabled = true
})
rb.assert(logging.result.success == true, "Enabling job logging failed.")

local speedSet = rb.call("rimworld/set_time_speed", {
  speed = speed
})
rb.assert(speedSet.result.success == true, "Setting time speed failed.")

local forceAction = rb.call("achtung/force_work_at_cell", {
  x = targetX,
  z = targetZ,
  labelContains = labelContains
})
rb.assert(forceAction.result.success == true, "Force work action failed.")

local forcedImmediately = rb.call("achtung/get_selected_pawn_forced_state")
local initialCompletionCell = rb.call("rimworld/get_cells_info", {
  x = completionX,
  z = completionZ,
  width = 1,
  height = 1
})
local completionCellStartedBuilt = initialCompletionCell.result.cells[1].thingCount > 0
  and initialCompletionCell.result.cells[1].things[1].isFrame == false
  and initialCompletionCell.result.cells[1].things[1].defName == "Wall"
rb.assert(completionCellStartedBuilt == false, "Completion cell was already built before force work started.")

local completion = rb.poll("rimworld/get_cells_info", {
  x = rectX,
  z = rectZ,
  width = rectWidth,
  height = rectHeight
}, {
  timeoutMs = 15000,
  pollIntervalMs = 100,
  condition = {
    all = {
      { path = "result.cellCount", equals = rectWidth * rectHeight },
      {
        path = "result.cells",
        allItems = {
          path = "solidThingDefs[0]",
          equals = "Wall"
        }
      }
    }
  }
})

rb.print("completion_attempts", completion.attempts)

rb.call("rimworld/pause_game", {
  pause = true
})

local forcedAfterRun = rb.call("achtung/get_selected_pawn_forced_state")
local finalCells = rb.call("rimworld/get_cells_info", {
  x = rectX,
  z = rectZ,
  width = rectWidth,
  height = rectHeight
})
local warnings = rb.call("rimbridge/list_logs", {
  afterSequence = logging.result.logCursor,
  minimumLevel = "warning",
  limit = 100
})

local builtCount = 0
local remainingCount = 0

for _, cell in ipairs(finalCells.result.cells) do
  local hasWall = cell.thingCount > 0 and cell.solidThingDefs[1] == "Wall"
  if hasWall then
    builtCount = builtCount + 1
  else
    remainingCount = remainingCount + 1
  end
end

return {
  saveName = saveName,
  pawnName = pawnName,
  target = {
    x = targetX,
    z = targetZ
  },
  rect = {
    x = rectX,
    z = rectZ,
    width = rectWidth,
    height = rectHeight
  },
  completionCell = {
    x = completionX,
    z = completionZ
  },
  completionCellStartedBuilt = completionCellStartedBuilt,
  speed = speedSet.result.timeSpeed,
  forceAction = forceAction.result,
  forcedImmediately = forcedImmediately.result,
  forcedAfterRun = forcedAfterRun.result,
  completionAttempts = completion.attempts,
  completionProbe = completion.result,
  builtCount = builtCount,
  remainingCount = remainingCount,
  allBuilt = remainingCount == 0,
  cells = finalCells.result.cells,
  warningLogs = warnings.result.logs
}
