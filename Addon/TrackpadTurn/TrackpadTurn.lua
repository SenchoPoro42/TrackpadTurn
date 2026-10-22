-- TrackpadTurn: two-finger trackpad turning for World of Warcraft on macOS.
--
-- BREADCRUMB: the TrackpadTurn Mac app (Sources/TrackpadTurnCore/WowSignalProtocol.swift)
-- holds the swipe key down for the whole two-finger swipe, taps the zoom keys while you pinch,
-- and posts mouse movement during the swipe. Mouse movement only turns the camera while
-- mouselook is on, so this addon decides per swipe: over the 3D world it starts mouselook
-- (camera and character turn, like holding the right mouse button); over a frame it does
-- nothing and the frame scrolls as usual.
-- A held two-finger press reaches WoW as the right button plus the held forward key, which runs
-- Blizzard's own MOVEFORWARD binding: the same as holding both mouse buttons.

local ADDON_NAME = ...

TrackpadTurn = TrackpadTurn or {}
local TrackpadTurn = TrackpadTurn

local PROTOCOL_VERSION = 2
-- Keys nobody presses by hand. Must match WowSignalProtocol.SignalKey.wowKeyName.
local SIGNAL_KEYS = {
	TRACKPADTURN_SWIPE = "F19",
	TRACKPADTURN_ZOOMIN = "F17",
	TRACKPADTURN_ZOOMOUT = "F18",
}
-- Held while a two-finger click is held. It is bound straight to one of Blizzard's own
-- bindings, chosen in Settings → AddOns: movement is protected, so only the game's secure
-- bindings may start it. (SetMouselookOverrideBinding would gate it to mouselook, but it only
-- remaps mouse buttons; a key bound through it does nothing.)
local HOLD_KEY = "F16"
local HOLD_BINDINGS = {
	run = "MOVEFORWARD", -- run while held
	autorun = "TOGGLEAUTORUN", -- each hold turns autorun on or off
}
-- Yards per pinch step. The app's "Zoom speed" slider decides how many steps a pinch makes.
local ZOOM_YARDS_PER_STEP = 0.5
-- Wheel ticks from a swipe can still arrive just after the fingers lift (the trackpad's glide,
-- or ticks WoW held back during mouselook). Ticks closer together than this still belong to
-- the swipe; a real wheel scroll after a pause zooms as normal.
local SWIPE_WHEEL_TAIL_SECONDS = 0.25

local GetAddOnMetadata = C_AddOns and C_AddOns.GetAddOnMetadata or GetAddOnMetadata
local VERSION = GetAddOnMetadata(ADDON_NAME, "Version") or "?"

local swipe = {
	isDown = false,
	-- True only when this swipe started mouselook, so we never stop one the player started.
	startedMouselook = false,
	-- Wheel ticks before this time still belong to the last swipe.
	wheelTailUntil = 0,
	wheelTicksAfterRelease = 0,
}
local stats = {
	swipes = 0,
	turningSwipes = 0,
	zoomSteps = 0,
	wheelTicksIgnoredAfterRelease = 0,
	lastSignalAt = nil,
}

local function Print(message)
	DEFAULT_CHAT_FRAME:AddMessage("|cff4fc3f7TrackpadTurn|r: " .. message)
end

local function Debug(message)
	if TrackpadTurnDB and TrackpadTurnDB.debug then
		Print("|cff999999" .. message .. "|r")
	end
end

-- Cursor and world checks -----------------------------------------------------

local function TopMouseFocus()
	if GetMouseFoci then
		local foci = GetMouseFoci()
		return foci and foci[1]
	end
	return GetMouseFocus and GetMouseFocus()
end

local function DescribeRegion(region)
	if not region then
		return "nothing"
	end
	return (region.GetName and region:GetName()) or tostring(region)
end

-- Nameplates and other world-anchored frames live under WorldFrame; the UI lives under UIParent.
local function IsPartOfWorld(region)
	while region do
		if region == WorldFrame then
			return true
		end
		-- Forbidden frames (enemy nameplates in instances) cannot be inspected; treat them as world.
		if region.IsForbidden and region:IsForbidden() then
			return true
		end
		region = region:GetParent()
	end
	return false
end

-- Why a swipe must leave the camera alone, or nil when it may turn.
local function ReasonNotToTurn()
	if IsMouselooking() then
		return "already mouselooking"
	end
	if SpellIsTargeting and SpellIsTargeting() then
		return "placing a spell"
	end
	if GetCursorInfo() then
		return "carrying something on the cursor"
	end
	local focus = TopMouseFocus()
	if focus and not IsPartOfWorld(focus) then
		return "cursor over " .. DescribeRegion(focus)
	end
	return nil
end

-- The release of the swipe key can be lost, e.g. when a chat box takes the keyboard mid-swipe.
-- WoW tracks the key's real state, so end the swipe once it is no longer held. Armed only after
-- the key has been seen held, so a client that cannot report it never cuts swipes short.
local releaseWatchdog = CreateFrame("Frame")
releaseWatchdog:Hide()
releaseWatchdog:SetScript("OnUpdate", function(self)
	if IsKeyDown(SIGNAL_KEYS.TRACKPADTURN_SWIPE) then
		self.sawKeyHeld = true
	elseif self.sawKeyHeld then
		Debug("swipe release was lost; ending the swipe")
		TrackpadTurn.OnSwipeKey("up")
	end
end)

-- Binding handlers (called from Bindings.xml) --------------------------------

function TrackpadTurn.OnSwipeKey(keystate)
	stats.lastSignalAt = GetTime()

	if keystate == "down" then
		-- A second "down" means the last release never arrived (e.g. WoW lost focus mid-swipe).
		-- Keep the mouselook we already own and start over.
		if swipe.isDown and swipe.startedMouselook and IsMouselooking() then
			return
		end
		if swipe.wheelTicksAfterRelease > 0 then
			Debug(("last release: ignored %d wheel ticks"):format(swipe.wheelTicksAfterRelease))
		end
		swipe.isDown = true
		swipe.startedMouselook = false
		swipe.wheelTicksAfterRelease = 0
		stats.swipes = stats.swipes + 1
		releaseWatchdog.sawKeyHeld = false
		releaseWatchdog:Show()

		local reason = ReasonNotToTurn()
		if reason then
			Debug("swipe ignored: " .. reason)
			return
		end
		MouselookStart()
		swipe.startedMouselook = true
		stats.turningSwipes = stats.turningSwipes + 1
		Debug("swipe turning")
	else
		if not swipe.isDown then
			return
		end
		if swipe.startedMouselook and IsMouselooking() then
			MouselookStop()
		end
		swipe.isDown = false
		swipe.startedMouselook = false
		swipe.wheelTailUntil = GetTime() + SWIPE_WHEEL_TAIL_SECONDS
		releaseWatchdog:Hide()
	end
end

function TrackpadTurn.OnZoomKey(direction)
	stats.lastSignalAt = GetTime()
	stats.zoomSteps = stats.zoomSteps + 1
	if direction > 0 then
		CameraZoomIn(ZOOM_YARDS_PER_STEP)
	else
		CameraZoomOut(ZOOM_YARDS_PER_STEP)
	end
end

local ZOOM_BINDINGS = { [1] = "CAMERAZOOMIN", [-1] = "CAMERAZOOMOUT" }

-- Stands in for the default CAMERAZOOMIN/OUT wheel binding. Ticks that belong to a two-finger
-- swipe must never zoom (pinch zooms instead), not even the ones that trail the release.
-- Frames that scroll (chat, quest log) get the wheel before bindings, so they still scroll.
--
-- It zooms with CameraZoomIn/Out itself rather than RunBinding: Retail-engine clients (Forever)
-- zoom with MoveViewInStart, which addon code may not reach, and the game blocks the call.
function TrackpadTurn.OnWheel(direction)
	if swipe.isDown then
		return
	end
	local now = GetTime()
	if now < swipe.wheelTailUntil then
		-- Still trailing the swipe: keep the window open while ticks keep coming.
		swipe.wheelTailUntil = now + SWIPE_WHEEL_TAIL_SECONDS
		swipe.wheelTicksAfterRelease = swipe.wheelTicksAfterRelease + 1
		stats.wheelTicksIgnoredAfterRelease = stats.wheelTicksIgnoredAfterRelease + 1
		return
	end
	if direction > 0 then
		CameraZoomIn(1.0)
	else
		CameraZoomOut(1.0)
	end
end

-- Bindings --------------------------------------------------------------------

local bindingOwner = CreateFrame("Frame")
local applyPendingAfterCombat = false
local appliedWheelState = nil

-- Every modifier combination, in WoW's chord order (ALT-CTRL-SHIFT-META-KEY): the app reports
-- the modifiers you really hold, so Shift+swipe arrives as SHIFT-F19.
local MODIFIER_PREFIXES = {}
do
	local modifiers = { "ALT-", "CTRL-", "SHIFT-", "META-" }
	for mask = 0, 15 do
		local prefix = ""
		for index, modifier in ipairs(modifiers) do
			if bit.band(mask, bit.lshift(1, index - 1)) ~= 0 then
				prefix = prefix .. modifier
			end
		end
		MODIFIER_PREFIXES[#MODIFIER_PREFIXES + 1] = prefix
	end
end

-- Only take over the plain wheel while it still does the default camera zoom.
local function WheelState()
	return {
		up = GetBindingAction("MOUSEWHEELUP") == ZOOM_BINDINGS[1],
		down = GetBindingAction("MOUSEWHEELDOWN") == ZOOM_BINDINGS[-1],
	}
end

local function ApplyBindings()
	if InCombatLockdown() then
		applyPendingAfterCombat = true
		return
	end
	applyPendingAfterCombat = false

	ClearOverrideBindings(bindingOwner)
	local holdAction = TrackpadTurnDB.holdTogglesAutorun and HOLD_BINDINGS.autorun or HOLD_BINDINGS.run
	local keyActions = { [HOLD_KEY] = holdAction }
	for action, key in pairs(SIGNAL_KEYS) do
		keyActions[key] = action
	end
	for key, action in pairs(keyActions) do
		for _, prefix in ipairs(MODIFIER_PREFIXES) do
			-- pcall: an unknown chord on some client must not stop the rest from binding.
			pcall(SetOverrideBinding, bindingOwner, true, prefix .. key, action)
		end
	end

	local wheel = WheelState()
	if wheel.up then
		SetOverrideBinding(bindingOwner, false, "MOUSEWHEELUP", "TRACKPADTURN_WHEELUP")
	end
	if wheel.down then
		SetOverrideBinding(bindingOwner, false, "MOUSEWHEELDOWN", "TRACKPADTURN_WHEELDOWN")
	end
	appliedWheelState = wheel
end

-- Settings → AddOns → TrackpadTurn ------------------------------------------

local settingsCategory

-- Follows the pattern in Blizzard_Settings_Shared/Blizzard_ImplementationReadme.lua.
local function RegisterSettings()
	if not (Settings and Settings.RegisterVerticalLayoutCategory) then
		return
	end
	settingsCategory = Settings.RegisterVerticalLayoutCategory("TrackpadTurn")

	local holdSetting = Settings.RegisterAddOnSetting(settingsCategory, "TRACKPADTURN_HOLD_TOGGLES_AUTORUN",
		"holdTogglesAutorun", TrackpadTurnDB, Settings.VarType.Boolean, "Two-finger hold toggles autorun", false)
	holdSetting:SetValueChangedCallback(function()
		ApplyBindings()
		if applyPendingAfterCombat then
			Print("the new two-finger hold setting applies when combat ends.")
		end
	end)
	Settings.CreateCheckbox(settingsCategory, holdSetting,
		"Off: you run forward while you hold a two-finger click.\n"
		.. "On: each held two-finger click turns autorun on or off.\n"
		.. "Either way, swipe to steer. A quick two-finger click stays a normal right-click.")

	Settings.RegisterAddOnCategory(settingsCategory)
end

-- Status ----------------------------------------------------------------------

local function PrintStatus()
	Print(("v%s (protocol %d)"):format(VERSION, PROTOCOL_VERSION))
	if stats.lastSignalAt then
		Print(("last signal %.0fs ago · %d swipes (%d turned) · %d zoom steps · %d wheel ticks ignored after release"):format(
			GetTime() - stats.lastSignalAt, stats.swipes, stats.turningSwipes, stats.zoomSteps,
			stats.wheelTicksIgnoredAfterRelease))
	else
		Print("no signal from the TrackpadTurn app yet. Is it running, enabled and allowed under "
			.. "System Settings > Privacy & Security > Accessibility?")
	end
	Print("two-finger hold: " .. (TrackpadTurnDB.holdTogglesAutorun and "toggles autorun" or "run while held")
		.. " (/tpt settings to change).")
	local wheel = appliedWheelState or WheelState()
	if not (wheel.up and wheel.down) then
		Print("your mouse wheel has a custom binding, so swipes may also trigger it.")
	end
	if TrackpadTurnDB.debug then
		Print("debug messages are on (/tpt debug to turn off).")
	end
end

SLASH_TRACKPADTURN1 = "/trackpadturn"
SLASH_TRACKPADTURN2 = "/tpt"
SlashCmdList.TRACKPADTURN = function(input)
	local command = strtrim(input or ""):lower()
	if command == "debug" then
		TrackpadTurnDB.debug = not TrackpadTurnDB.debug
		Print("debug messages " .. (TrackpadTurnDB.debug and "on" or "off") .. ".")
	elseif command == "settings" and settingsCategory then
		Settings.OpenToCategory(settingsCategory:GetID())
	else
		PrintStatus()
	end
end

-- Events ----------------------------------------------------------------------

-- The game's own "blocked" popup does not say which call it blocked; name it once per call.
local reportedBlockedCalls = {}

bindingOwner:RegisterEvent("ADDON_LOADED")
bindingOwner:RegisterEvent("PLAYER_LOGIN")
bindingOwner:RegisterEvent("UPDATE_BINDINGS")
bindingOwner:RegisterEvent("PLAYER_REGEN_ENABLED")
bindingOwner:RegisterEvent("ADDON_ACTION_FORBIDDEN")
bindingOwner:RegisterEvent("ADDON_ACTION_BLOCKED")
bindingOwner:SetScript("OnEvent", function(_, event, ...)
	if event == "ADDON_LOADED" then
		if ... == ADDON_NAME then
			TrackpadTurnDB = TrackpadTurnDB or {}
			RegisterSettings()
			bindingOwner:UnregisterEvent("ADDON_LOADED")
		end
	elseif event == "PLAYER_LOGIN" then
		ApplyBindings()
		if not TrackpadTurnDB.welcomed then
			TrackpadTurnDB.welcomed = true
			Print("swipe two fingers over the world to turn, pinch to zoom. /tpt shows status.")
		end
	elseif event == "UPDATE_BINDINGS" then
		-- Fires for our own override changes too; only react when the player rebinds the wheel.
		local wheel = WheelState()
		if appliedWheelState and (wheel.up ~= appliedWheelState.up or wheel.down ~= appliedWheelState.down) then
			ApplyBindings()
		end
	elseif event == "PLAYER_REGEN_ENABLED" then
		if applyPendingAfterCombat then
			ApplyBindings()
		end
	elseif event == "ADDON_ACTION_FORBIDDEN" or event == "ADDON_ACTION_BLOCKED" then
		local addonName, blockedCall = ...
		if addonName == ADDON_NAME and not reportedBlockedCalls[tostring(blockedCall)] then
			reportedBlockedCalls[tostring(blockedCall)] = true
			Print(("the game blocked TrackpadTurn from calling %s. Please report this, it is a bug."):format(
				tostring(blockedCall)))
		end
	end
end)
