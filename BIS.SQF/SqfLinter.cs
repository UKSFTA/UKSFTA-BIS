#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BIS.SQF;

public enum SqfLintSeverity
{
    Error,
    Warning,
    Help,
}

/// <summary>Describes an automatic source fix for a lint diagnostic.</summary>
public sealed record SqfLintFix(int Offset, int Length, string ReplacementText);

public readonly struct SqfLintDiagnostic
{
    public string Code { get; }
    public SqfLintSeverity Severity { get; }
    public string Message { get; }
    public string File { get; }
    public int Line { get; }
    public int Column { get; }
    public SqfLintFix? Fix { get; init; }

    public SqfLintDiagnostic(string code, SqfLintSeverity severity, string message,
        string file = "", int line = 0, int column = 0)
    {
        Code = code;
        Severity = severity;
        Message = message;
        File = file;
        Line = line;
        Column = column;
    }

    public override string ToString()
    {
        var loc = string.IsNullOrEmpty(File) ? $"{Line},{Column}" : $"{File}({Line},{Column})";
        return $"[{Code}] {Severity}: {Message} at {loc}";
    }
}

/// <summary>
/// Linter for SQF source files. Walks the AST and applies lint rules.
/// </summary>
public class SqfLinter
{
    private readonly List<SqfLintDiagnostic> _diagnostics = new();
    private string _sourceText = "";
    // Known SQF commands with correct casing — organized by category
    // Expanded from ~130 to ~600+ covering commonly-used commands
    private static readonly HashSet<string> KnownCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        #region Control Flow & Keywords
        "break",
        "breakOut",
        "breakTo",
        "breakWith",
        "call",
        "case",
        "catch",
        "continueWith",
        "default",
        "do",
        "else",
        "exec",
        "execFSM",
        "execVM",
        "exitWith",
        "false",
        "for",
        "forEach",
        "if",
        "invoke",
        "isEqualTo",
        "isEqualType",
        "isEqualTypeAll",
        "isNil",
        "isNotEqualTo",
        "isNull",
        "nil",
        "Nothing",
        "param",
        "params",
        "private",
        "scopeName",
        "sleep",
        "spawn",
        "switch",
        "then",
        "throw",
        "time",
        "true",
        "try",
        "typeName",
        "typeof",
        "typeOf",
        "uiSleep",
        "waitUntil",
        "while",
        #endregion

        #region Conversions
        "composeText",
        "formatText",
        "text",
        "toArray",
        "toBool",
        "toFloat",
        "toInt",
        "toNumber",
        "toString",
        "toText",
        "toVector",
        #endregion

        #region Math & Numeric
        "abs",
        "acos",
        "asin",
        "atan",
        "atan2",
        "bracketPhysics",
        "ceil",
        "cos",
        "deg",
        "exp",
        "floor",
        "inverseLerp",
        "lerp",
        "linearConversion",
        "ln",
        "log",
        "matrixMultiply",
        "matrixTranspose",
        "max",
        "min",
        "parseFloat",
        "parseInt",
        "parseNumber",
        "pi",
        "rad",
        "randInt",
        "random",
        "round",
        "selectRandomWeighted",
        "setBracketPhysics",
        "setVectorDir",
        "setVectorUp",
        "sin",
        "sqrt",
        "tan",
        "vectorAdd",
        "vectorCrossProduct",
        "vectorDiff",
        "vectorDir",
        "vectorDistance",
        "vectorDistanceSqr",
        "vectorDotProduct",
        "vectorMagnitude",
        "vectorMagnitudeSqr",
        "vectorNormalized",
        "vectorUp",
        #endregion

        #region String Operations
        "count",
        "deleteRange",
        "find",
        "forEachMemberIndex",
        "format",
        "formatText",
        "hashValue",
        "in",
        "insert",
        "match",
        "matchCount",
        "parseText",
        "regex",
        "regexFind",
        "regexReplace",
        "replace",
        "replaceAll",
        "resize",
        "reverse",
        "select",
        "splitString",
        "str",
        "stringCut",
        "stringIndexOf",
        "stringLastIndexOf",
        "substr",
        "toArray",
        "toFixed",
        "toLower",
        "toString",
        "toUpper",
        "trim",
        "trimLeft",
        "trimRight",
        #endregion

        #region Array Operations
        "apply",
        "arrayIntersect",
        "count",
        "deleteAt",
        "deleteRange",
        "find",
        "findIf",
        "flatten",
        "forEach",
        "get",
        "in",
        "intersect",
        "plusSet",
        "pushBack",
        "pushBackUnique",
        "resize",
        "reverse",
        "selectMax",
        "selectMin",
        "selectRandom",
        "set",
        "sort",
        "toArray",
        #endregion

        #region HashMap / Namespace
        "allVariables",
        "copyHashMap",
        "count",
        "createHashMap",
        "createHashMapFromArray",
        "createHashMapObject",
        "currentNamespace",
        "deleteAt",
        "get",
        "getVariable",
        "insert",
        "keys",
        "localNamespace",
        "mergeHashMap",
        "missionProfileNamespace",
        "parsingNamespace",
        "serverNamespace",
        "setVariable",
        "uiNamespace",
        "values",
        #endregion

        #region Config
        "binarize",
        "binarizeFile",
        "campaignConfigFile",
        "configChildren",
        "configClasses",
        "configEntry",
        "configFile",
        "configHierarchy",
        "configName",
        "configNull",
        "configOf",
        "configPatch",
        "configProperties",
        "configSourceAddonList",
        "configSourceMod",
        "configSourceModList",
        "getArray",
        "getNumber",
        "getText",
        "inheritsFrom",
        "isArray",
        "isClass",
        "isNumber",
        "isText",
        "missionConfigFile",
        "select",
        #endregion

        #region Object / Unit
        "aimPos",
        "alive",
        "allDead",
        "allDeadMen",
        "allGroups",
        "allMissionObjects",
        "allowDammage",
        "allowFleeing",
        "allowSprint",
        "allPlayers",
        "allSimpleObjects",
        "allUnits",
        "allVariables",
        "attachedObjects",
        "attachedTo",
        "attachTo",
        "boundingBox",
        "boundingBoxReal",
        "boundingCenter",
        "canMove",
        "canStand",
        "captive",
        "createGroup",
        "createUnit",
        "createVehicle",
        "createVehicleCrew",
        "createVehicleLocal",
        "currentMagazine",
        "currentVisionMode",
        "currentWeapon",
        "deleteGroup",
        "detach",
        "disableAI",
        "disableCollisionWith",
        "disableConversation",
        "distance",
        "distance2D",
        "doFire",
        "doMove",
        "doStop",
        "doWatch",
        "enableAI",
        "enableAimPrecision",
        "enableCollisionWith",
        "enableFatigue",
        "enableSimulation",
        "enableSimulationGlobal",
        "enableStamina",
        "engineOn",
        "eyeDirection",
        "eyePos",
        "faction",
        "getCenterOfMass",
        "getMass",
        "getObjectMaterial",
        "getObjectProxy",
        "getObjectTexture",
        "getObjectTextures",
        "getPos",
        "getPosASL",
        "getPosASLW",
        "getPosATL",
        "getVariable",
        "hasInterface",
        "hideObject",
        "hideObjectGlobal",
        "isCollisionLightOn",
        "isDamageAllowed",
        "isDedicated",
        "isEngineOn",
        "isHidden",
        "isKindOf",
        "isLightOn",
        "isObjectHidden",
        "isObjectZoomable",
        "isPlayer",
        "isServer",
        "isVehicle",
        "isVehicleCargo",
        "lineIntersects",
        "lineIntersectsObjs",
        "lineIntersectsWith",
        "local",
        "lock",
        "locked",
        "lookAt",
        "lookAtPos",
        "modelToWorld",
        "modelToWorldVisual",
        "nearestBuilding",
        "nearestLocation",
        "nearestLocations",
        "nearestObject",
        "nearestObjects",
        "nearestTerrainObjects",
        "nearObjects",
        "nearRoads",
        "nearRoadsOnly",
        "nearSupplies",
        "nearTargets",
        "nearTerrainObjects",
        "objectParent",
        "owner",
        "player",
        "rank",
        "rating",
        "removeAllActions",
        "screenToWorld",
        "selectPlayer",
        "setAllowDamage",
        "setBehaviour",
        "setCaptive",
        "setCenterOfMass",
        "setCollisionLight",
        "setCombatMode",
        "setDamage",
        "setEngineOn",
        "setFormation",
        "setFuel",
        "setLightMode",
        "setMass",
        "setObjectMaterial",
        "setObjectProxy",
        "setObjectTexture",
        "setObjectTextures",
        "setOwner",
        "setPlateNumber",
        "setPos",
        "setPosASL",
        "setPosASLW",
        "setPosATL",
        "setSkill",
        "setSpeedMode",
        "setUnconscious",
        "setUnitPos",
        "setUnlock",
        "setVariable",
        "setVehicleAmmo",
        "setVehiclePosition",
        "setVehicleRadio",
        "setVehicleVarName",
        "side",
        "sizeOf",
        "terrainIntersect",
        "terrainIntersectASL",
        "vehicle",
        "vehicleAmmo",
        "vehicleRadio",
        "vehicles",
        "vehicleVarName",
        "visiblePosition",
        "visiblePositionASL",
        "worldToModel",
        "worldToModelVisual",
        "worldToScreen",
        #endregion

        #region Weapon / Gear
        "addBackpack",
        "addBackpackCargo",
        "addBackpackCargoGlobal",
        "addBackpackGlobal",
        "addGoggles",
        "addHeadgear",
        "addItem",
        "addItemCargo",
        "addItemCargoGlobal",
        "addItemGlobal",
        "addMagazine",
        "addMagazineCargo",
        "addMagazineCargoGlobal",
        "addMagazineGlobal",
        "addUniform",
        "addVest",
        "addWeapon",
        "addWeaponCargo",
        "addWeaponCargoGlobal",
        "addWeaponGlobal",
        "addWeaponItem",
        "addWeaponWithAttachments",
        "ammo",
        "assignedGoggles",
        "assignedItems",
        "backpack",
        "backpackItems",
        "binocular",
        "canAdd",
        "canAddItemToBackpack",
        "canAddItemToUniform",
        "canAddItemToVest",
        "currentMagazine",
        "currentMuzzle",
        "currentVehicle",
        "currentVisionMode",
        "currentWeapon",
        "forceAddUniform",
        "getAmmoCargo",
        "getContainerMaxLoad",
        "getMagazineCargo",
        "getUnitLoadout",
        "goggles",
        "handgunItems",
        "handgunMagazine",
        "handgunWeapon",
        "hasWeapon",
        "headgear",
        "hmd",
        "items",
        "linkedItems",
        "load",
        "loadAbs",
        "loadBackpack",
        "loadUniform",
        "loadVest",
        "magazines",
        "magazinesAmmo",
        "magazinesAmmoFull",
        "magazinesDetail",
        "magazinesDetailBackpack",
        "magazinesDetailUniform",
        "magazinesDetailVest",
        "primaryWeapon",
        "primaryWeaponItems",
        "primaryWeaponMagazine",
        "removeAllAssignedItems",
        "removeAllContainers",
        "removeAllHandgunItems",
        "removeAllItems",
        "removeAllMagazines",
        "removeAllPrimaryWeaponItems",
        "removeAllSecondaryWeaponItems",
        "removeAllWeapons",
        "removeBackpack",
        "removeGoggles",
        "removeHeadgear",
        "removeItem",
        "removeItemGlobal",
        "removeMagazine",
        "removeMagazineGlobal",
        "removeUniform",
        "removeVest",
        "removeWeapon",
        "removeWeaponGlobal",
        "removeWeaponItem",
        "secondaryWeapon",
        "secondaryWeaponItems",
        "secondaryWeaponMagazine",
        "setAmmo",
        "setAmmoCargo",
        "setAmmoOnPylon",
        "setMagazineCargo",
        "setUnitLoadout",
        "setWeaponReloadingTime",
        "uniform",
        "uniformItems",
        "vest",
        "vestItems",
        "weaponAccessories",
        "weaponDirection",
        "weaponInfo",
        "weaponItems",
        "weaponReloadingTime",
        "weapons",
        "weaponState",
        #endregion

        #region Marker
        "createLocation",
        "createMarker",
        "createMarkerLocal",
        "deleteLocation",
        "deleteMarker",
        "getMarkerColor",
        "getMarkerPos",
        "locationNull",
        "locationPosition",
        "markerAlpha",
        "markerBrush",
        "markerChannel",
        "markerColor",
        "markerDir",
        "markerPos",
        "markerShape",
        "markerSize",
        "markerText",
        "markerType",
        "setMarkerAlpha",
        "setMarkerAlphaLocal",
        "setMarkerBrush",
        "setMarkerBrushLocal",
        "setMarkerColor",
        "setMarkerColorLocal",
        "setMarkerDir",
        "setMarkerDirLocal",
        "setMarkerPos",
        "setMarkerPosLocal",
        "setMarkerShape",
        "setMarkerShapeLocal",
        "setMarkerSize",
        "setMarkerSizeLocal",
        "setMarkerText",
        "setMarkerTextLocal",
        "setMarkerType",
        "setMarkerTypeLocal",
        #endregion

        #region GUI / Display
        "closeDialog",
        "createDialog",
        "createDisplay",
        "ctrlAddEventHandler",
        "ctrlAutoScrollDelay",
        "ctrlAutoScrollRewind",
        "ctrlAutoScrollSpeed",
        "ctrlClassName",
        "ctrlCreate",
        "ctrlDelete",
        "ctrlEnable",
        "ctrlEnabled",
        "ctrlFade",
        "ctrlHide",
        "ctrlHTMLLoaded",
        "ctrlIDC",
        "ctrlMap",
        "ctrlMapAnimAdd",
        "ctrlMapAnimClear",
        "ctrlMapAnimCommit",
        "ctrlMapCursor",
        "ctrlMapMouseMove",
        "ctrlMapMouseOver",
        "ctrlMapScreenToWorld",
        "ctrlMapWorldToScreen",
        "ctrlParent",
        "ctrlPosition",
        "ctrlRemoveEventHandler",
        "ctrlSetBackgroundColor",
        "ctrlSetEventHandler",
        "ctrlSetFade",
        "ctrlSetPosition",
        "ctrlSetScale",
        "ctrlSetText",
        "ctrlSetTextColor",
        "ctrlSetTooltip",
        "ctrlShow",
        "ctrlText",
        "ctrlType",
        "ctrlVisible",
        "cutFadeOut",
        "cutObj",
        "cutRsc",
        "cutText",
        "destroyDialog",
        "displayAddEventHandler",
        "displayCtrl",
        "displayedCommand",
        "displayRemoveEventHandler",
        "displaySetEventHandler",
        "displaySetMousePosition",
        "findDialog",
        "findDisplay",
        "htmlLoad",
        "lbAdd",
        "lbClear",
        "lbCurSel",
        "lbData",
        "lbDelete",
        "lbPicture",
        "lbSelection",
        "lbSetColor",
        "lbSetCurSel",
        "lbSetData",
        "lbSetPicture",
        "lbSetPictureColor",
        "lbSetPictureColorSelected",
        "lbSetPictureRight",
        "lbSetSelectColor",
        "lbSetSelectColorRight",
        "lbSetText",
        "lbSetTooltip",
        "lbSetValue",
        "lbSize",
        "lbSort",
        "lbSortByValue",
        "lbText",
        "lbValue",
        "lnbAddArray",
        "lnbAddColumn",
        "lnbAddRow",
        "lnbClear",
        "lnbColor",
        "lnbCurSelRow",
        "lnbData",
        "lnbDeleteRow",
        "lnbGetText",
        "lnbSetColor",
        "lnbSetColumnsPos",
        "lnbSetColumnText",
        "lnbSetData",
        "lnbSetText",
        "lnbSetValue",
        "lnbText",
        "lnbValue",
        "mousePosition",
        "openMap",
        "openMapEnlarge",
        "progressSetPosition",
        "setMousePosition",
        "sliderPosition",
        "sliderRange",
        "sliderSpeed",
        "tvAdd",
        "tvClear",
        "tvCollapse",
        "tvCollapseAll",
        "tvCount",
        "tvCurSel",
        "tvData",
        "tvDelete",
        "tvExpand",
        "tvExpandAll",
        "tvPicture",
        "tvSelection",
        "tvSetCurSel",
        "tvSetData",
        "tvSetPicture",
        "tvSetText",
        "tvSetTooltip",
        "tvSortAll",
        "tvSortByValue",
        "tvText",
        "tvValue",
        #endregion

        #region Event Handlers
        "addEventHandler",
        "addMissionEventHandler",
        "addMPEventHandler",
        "addMusicEventHandler",
        "addPlayerEventHandler",
        "addPublicVariableEventHandler",
        "addUserActionEventHandler",
        "displaySetEventHandler",
        "removeAllEventHandlers",
        "removeAllMissionEventHandlers",
        "removeAllPublicVariableEventHandlers",
        "removeEventHandler",
        "removePublicVariableEventHandler",
        #endregion

        #region Animation / Movement
        "action",
        "addWeapon",
        "animationPhase",
        "animationState",
        "enableFatigue",
        "enableStamina",
        "playAction",
        "playActionNow",
        "playGesture",
        "playMove",
        "playMoveNow",
        "removeWeapon",
        "setFace",
        "setFaceAnimation",
        "setFacewear",
        "setIdentity",
        "setMimic",
        "setSpeaker",
        "setUnitPos",
        "switchAction",
        "switchCamera",
        "switchGesture",
        "switchMove",
        "switchWeapon",
        #endregion

        #region Music / Sound
        "addMusicEventHandler",
        "createSoundSource",
        "deleteSoundSource",
        "enableAudioFeature",
        "fadeMusic",
        "fadeRadio",
        "fadeSound",
        "fadeSpeech",
        "musicVolume",
        "playMusic",
        "playSound",
        "playSound3D",
        "playSoundUI",
        "preloadSound",
        "radioSpeech",
        "radioVolume",
        "say",
        "say2D",
        "say3D",
        "setMusicEffect",
        "setSoundEffect",
        "soundVolume",
        "stopMusic",
        #endregion

        #region Camera
        "addCamShake",
        "camCommand",
        "camCommit",
        "camCommitted",
        "camCreate",
        "camDestroy",
        "cameraEffect",
        "cameraEffectEnableHUD",
        "cameraView",
        "camPreload",
        "camPreloaded",
        "camPrepareBank",
        "camPrepareDir",
        "camPrepareDive",
        "camPrepareFocus",
        "camPrepareFOV",
        "camPreparePos",
        "camPrepareRelPos",
        "camPrepareTarget",
        "camSetBank",
        "camSetBearing",
        "camSetDir",
        "camSetDive",
        "camSetFocus",
        "camSetFOV",
        "camSetFOVRange",
        "camSetOffset",
        "camSetPos",
        "camSetRelPos",
        "camSetTarget",
        "camUseNVG",
        "enableCamShake",
        "resetCamShake",
        "setCamShake",
        #endregion

        #region World / Environment
        "date",
        "daytime",
        "fog",
        "fogParams",
        "forceWeatherChange",
        "forecast",
        "getObjectViewDistance",
        "getShadowDistance",
        "getWaterLeakiness",
        "gusts",
        "humidity",
        "lightnings",
        "missionName",
        "missionVersion",
        "moonIntensity",
        "nextWeatherChange",
        "objectViewDistance",
        "overcast",
        "preloadCamera",
        "preloadObject",
        "rain",
        "setDate",
        "setFog",
        "setFogParams",
        "setGusts",
        "setHumidity",
        "setObjectViewDistance",
        "setOvercast",
        "setRain",
        "setSimulatedWeatherChange",
        "setTerrainGrid",
        "setTimeMultiplier",
        "setTimeScale",
        "setViewDistance",
        "setWaves",
        "setWeatherDebounceTime",
        "setWind",
        "simulatedWeatherChange",
        "simulWeatherLayers",
        "sunOrMoon",
        "terrainGrid",
        "time",
        "timeMultiplier",
        "timeScale",
        "viewDistance",
        "waves",
        "weatherChanged",
        "weatherDebounceTime",
        "weatherForecast",
        "wind",
        "windDir",
        "windStr",
        "worldName",
        "worldSize",
        #endregion

        #region System / Debug
        "benchmark",
        "cutObj",
        "cutRsc",
        "cutText",
        "diag_activeMissionFSMs",
        "diag_activeScripts",
        "diag_activeSQFScripts",
        "diag_captureFrame",
        "diag_captureSlowFrame",
        "diag_codePerformance",
        "diag_drawMode",
        "diag_enable",
        "diag_exportTerrain",
        "diag_fps",
        "diag_fpsMin",
        "diag_frameNo",
        "diag_list",
        "diag_log",
        "diag_mergeConfigFile",
        "diag_record",
        "diag_setLightNew",
        "diag_snapshot",
        "diag_tickTime",
        "enableDebugConsole",
        "endMission",
        "exportCfgPatches",
        "failMission",
        "forceEnd",
        "getMissionConfig",
        "getMissionConfigValue",
        "hint",
        "hintC",
        "hintCadet",
        "hintMP",
        "hintMPWindow",
        "hintSilent",
        "importCfgPatches",
        "isDebugConsoleAllowed",
        "isFile",
        "log",
        "logEntities",
        "profileName",
        "profileNameSteam",
        "scriptName",
        "sleep",
        "systemChat",
        "testOldBracket",
        "titleCut",
        "titleFadeOut",
        "titleRsc",
        "titleShow",
        "titleText",
        "uiSleep",
        "waitUntil",
        #endregion

        #region Scheduler & Execution
        "call",
        "compile",
        "compileFinal",
        "compileScript",
        "exec",
        "execVM",
        "fsmDone",
        "fsmStates",
        "fsmTransition",
        "invoke",
        "isCompiledScript",
        "isFinal",
        "isNil",
        "isNull",
        "loadingScreen",
        "preloadCamera",
        "preloadSound",
        "preloadTitleRsc",
        "preloadVideo",
        "progressLoadingScreen",
        "scriptDone",
        "scriptName",
        "scriptNamePretty",
        "skipTime",
        "sleep",
        "spawn",
        "terminate",
        "terminateAll",
        "time",
        "uiSleep",
        "update",
        "waitUntil",
        #endregion

        #region File I/O
        "addonFiles",
        "binarize",
        "compileFinal",
        "compileScript",
        "copyFile",
        "deleteFile",
        "fileExists",
        "fileModified",
        "fileSize",
        "fileStatus",
        "loadConfig",
        "loadFile",
        "moveFile",
        "preprocessFile",
        "preprocessFileLineNumbers",
        "readFile",
        "renameFile",
        "writeFile",
        "xmlFile",
        #endregion

        #region Network / Multiplayer
        "addMissionEventHandler",
        "addPlayerEventHandler",
        "addPublicVariableEventHandler",
        "admin",
        "allPlayers",
        "BIS_fnc_MP",
        "BIS_fnc_MPexec",
        "clientOwner",
        "difficulty",
        "gameVersion",
        "getPlayerUID",
        "getPlayerUIDOld",
        "groupFromNetId",
        "hasInterface",
        "isDedicated",
        "isMultiplayer",
        "isMultiplayerSolo",
        "isServer",
        "netId",
        "objectFromNetId",
        "playersNumber",
        "productVersion",
        "publicVariable",
        "publicVariableClient",
        "publicVariableServer",
        "remoteControl",
        "remoteControlled",
        "remoteExec",
        "remoteExecCall",
        "removeAllMissionEventHandlers",
        "removeAllPublicVariableEventHandlers",
        "removeMissionEventHandler",
        "removePublicVariableEventHandler",
        "serverCommand",
        "serverCommandAvailable",
        "serverCommandExecutable",
        "serverName",
        "serverTime",
        "setGroupOwner",
        "setOwner",
        #endregion

        #region Action / Interaction
        "actionIDs",
        "actionKeyImages",
        "actionKeys",
        "actionKeysImages",
        "addAction",
        "addEventHandler",
        "clearItemActions",
        "inputAction",
        "isActionOn",
        "removeAction",
        "removeAllActions",
        "removeEventHandler",
        "setUserActionText",
        #endregion

        #region Rating / Score
        "addRating",
        "addScore",
        "addScoreSide",
        "rank",
        "rankId",
        "rating",
        "score",
        "scoreSide",
        #endregion

        #region Side / Group
        "allDead",
        "allGroups",
        "blufor",
        "civilian",
        "createGroup",
        "createUnit",
        "east",
        "group",
        "groupFromNetId",
        "groupIconSelectable",
        "groupIconsVisible",
        "groupId",
        "groupOwner",
        "independent",
        "leader",
        "leaderboardDeInit",
        "leaderboardInit",
        "opfor",
        "resistance",
        "setGroupIcon",
        "setGroupIconParams",
        "setGroupIconsVisible",
        "setGroupId",
        "setGroupIdGlobal",
        "setGroupOwner",
        "sideEnemy",
        "sideFriendly",
        "sideLogic",
        "sideUnknown",
        "units",
        "west",
        #endregion

        #region Briefing / Map
        "createDiaryLink",
        "createDiaryRecord",
        "createDiarySubject",
        "createMarker",
        "deleteDiaryRecord",
        "deleteMarker",
        "diarySubjectExists",
        "getDiaryRecordText",
        "onBriefingGroup",
        "onBriefingNotes",
        "onBriefingPlan",
        "onMapSingleClick",
        "onPlayerConnected",
        "onPlayerDisconnected",
        "openMap",
        "openMapEnlarge",
        "playerCreateDiaryRecord",
        "setDiaryRecordText",
        "taskCreate",
        #endregion

        #region Task & Mission
        "briefingName",
        "createSimpleTask",
        "createTask",
        "currentTask",
        "deleteTask",
        "didJIP",
        "getClientState",
        "getClientStateNumber",
        "hasInterface",
        "isAutotest",
        "isDedicated",
        "isFilePatchingEnabled",
        "isJIP",
        "isMainMenu",
        "isMultiplayer",
        "isServer",
        "missionDifficulty",
        "missionNamespace",
        "missionStart",
        "productVersion",
        "setSimpleTaskDescription",
        "setSimpleTaskDestination",
        "setSimpleTaskTarget",
        "setSimpleTaskType",
        "setTaskAlwaysVisible",
        "setTaskDescription",
        "setTaskDestination",
        "setTaskMarkerOffset",
        "setTaskState",
        "setTaskTarget",
        "setTaskType",
        "simpleTask",
        "taskAlwaysVisible",
        "taskChildren",
        "taskCompleted",
        "taskCurrent",
        "taskDescription",
        "taskDestination",
        "taskHint",
        "taskMarkerOffset",
        "taskMarkers",
        "taskParent",
        "taskState",
        "taskTarget",
        "taskType",
        #endregion

        #region AI
        "addRating",
        "addWaypoint",
        "allowDammage",
        "allowFleeing",
        "allowsDamage",
        "allowSprint",
        "commandArtilleryFire",
        "commandFire",
        "commandMove",
        "commandStop",
        "commandSuppressiveFire",
        "commandTarget",
        "commandWatch",
        "damage",
        "deleteWaypoint",
        "disableAI",
        "doArtilleryFire",
        "doFire",
        "doFollow",
        "doMove",
        "doStop",
        "doSuppressiveFire",
        "doTarget",
        "doWatch",
        "enableAI",
        "forceSpeed",
        "fuel",
        "getWPPos",
        "inflame",
        "knowsAbout",
        "lookAt",
        "lookAtPos",
        "moving",
        "playMove",
        "rank",
        "ready",
        "reveal",
        "revealMine",
        "setAllowDamage",
        "setBehaviour",
        "setCaptive",
        "setCombatMode",
        "setFormation",
        "setFuel",
        "setRank",
        "setSkill",
        "setSkillArray",
        "setSkillRaw",
        "setSpeedMode",
        "setUnitAbility",
        "setUnitPos",
        "setUnloadInCombat",
        "setVehicleArmor",
        "setWaypointBehaviour",
        "setWaypointCombatMode",
        "setWaypointCompletionRadius",
        "setWaypointDescription",
        "setWaypointFormation",
        "setWaypointLoiterRadius",
        "setWaypointLoiterType",
        "setWaypointName",
        "setWaypointPosition",
        "setWaypointScript",
        "setWaypointSpeed",
        "setWaypointStatements",
        "setWaypointTimeout",
        "setWaypointType",
        "setWaypointVisible",
        "setWPPos",
        "skill",
        "skillRaw",
        "stop",
        "suppressFor",
        "switchMove",
        "targetKnowledge",
        "unitAbility",
        "unitPos",
        "unloadInCombat",
        "vehicleAmmoDef",
        "vehicleArmor",
        "vehicleRating",
        "waypointAttachVehicle",
        "waypointBehaviour",
        "waypointCombatMode",
        "waypointCompletionRadius",
        "waypointDescription",
        "waypointFormation",
        "waypointLoiterRadius",
        "waypointLoiterType",
        "waypointName",
        "waypointPosition",
        "waypoints",
        "waypointScript",
        "waypointSpeed",
        "waypointStatements",
        "waypointTimeout",
        "waypointType",
        "waypointVisible",
        #endregion

        #region Effects / Particles
        "chemlightAmbient",
        "chemlightDiffuse",
        "createSoundSource",
        "createVehicle",
        "createVehicleLocal",
        "deleteSoundSource",
        "deleteVehicle",
        "drop",
        "lightAttachObject",
        "particlesQuality",
        "setLightAmbient",
        "setLightAttenuation",
        "setLightBrightness",
        "setLightColor",
        "setLightDayLight",
        "setLightFlareMaxDistance",
        "setLightFlareSize",
        "setLightIntensity",
        "setLightUseFlare",
        "setParticleCircle",
        "setParticleClass",
        "setParticleFire",
        "setParticleFSM",
        "setParticleParams",
        "setParticleRandom",
        "setSmoke",
        #endregion

        #region SQL / DLC
        "activateKey",
        "deActivateKey",
        "enableSaving",
        "isKeyActive",
        "loadGame",
        "loadProfileNamespace",
        "saveGame",
        "saveProfileNamespace",
        "saveVar",
        #endregion

        #region BIS_ Functions
        "BIS_fnc_addToPairs",
        "BIS_fnc_addWeapon",
        "BIS_fnc_areEqual",
        "BIS_fnc_arrayPush",
        "BIS_fnc_arrayPushStack",
        "BIS_fnc_basicTask",
        "BIS_fnc_bitwiseAnd",
        "BIS_fnc_bitwiseNOT",
        "BIS_fnc_bitwiseOR",
        "BIS_fnc_bitwiseXOR",
        "BIS_fnc_cameraOld",
        "BIS_fnc_captive",
        "BIS_fnc_configPath",
        "BIS_fnc_consolidateArray",
        "BIS_fnc_contains",
        "BIS_fnc_CR",
        "BIS_fnc_createDiaryRecord",
        "BIS_fnc_createMarker",
        "BIS_fnc_createVehicle",
        "BIS_fnc_CtrlFitToTextHeight",
        "BIS_fnc_CtrSetScale",
        "BIS_fnc_CtrSetText",
        "BIS_fnc_dbAnomaly",
        "BIS_fnc_dbClassList",
        "BIS_fnc_debugConsoleEx",
        "BIS_fnc_decodeFlags",
        "BIS_fnc_decodeFlags2",
        "BIS_fnc_deleteMarker",
        "BIS_fnc_destroyVehicle",
        "BIS_fnc_dirTo",
        "BIS_fnc_displayColour",
        "BIS_fnc_drawCurvedPath",
        "BIS_fnc_effectKilled",
        "BIS_fnc_effectKilledAirDestruction",
        "BIS_fnc_encodeFlags",
        "BIS_fnc_encodeFlags2",
        "BIS_fnc_endMission",
        "BIS_fnc_enemyDetected",
        "BIS_fnc_exportCfgPatches",
        "BIS_fnc_findExtreme",
        "BIS_fnc_findInPairs",
        "BIS_fnc_findNestedElement",
        "BIS_fnc_fire",
        "BIS_fnc_getMarksmenDLC",
        "BIS_fnc_getParamValue",
        "BIS_fnc_goThere",
        "BIS_fnc_groupVehicles",
        "BIS_fnc_halo",
        "BIS_fnc_helicopterCue",
        "BIS_fnc_help",
        "BIS_fnc_helpSQF",
        "BIS_fnc_helpSQFElement",
        "BIS_fnc_hideObjectGlobal",
        "BIS_fnc_holdActionAdd",
        "BIS_fnc_holdActionRemove",
        "BIS_fnc_inAngleSector",
        "BIS_fnc_indicateEnemyMovement",
        "BIS_fnc_initParams",
        "BIS_fnc_injectAnimation",
        "BIS_fnc_isInBuilding",
        "BIS_fnc_isNight",
        "BIS_fnc_kbTellChatLog",
        "BIS_fnc_keyCode",
        "BIS_fnc_languages",
        "BIS_fnc_listPlayers",
        "BIS_fnc_liveIn",
        "BIS_fnc_livingUnits",
        "BIS_fnc_locationDescription",
        "BIS_fnc_log",
        "BIS_fnc_logFormat",
        "BIS_fnc_lookAt",
        "BIS_fnc_missionHandlers",
        "BIS_fnc_missionTasks",
        "BIS_fnc_moduleACE",
        "BIS_fnc_moduleCheck",
        "BIS_fnc_moduleMptType",
        "BIS_fnc_moduleMPType",
        "BIS_fnc_moduleRespawnType",
        "BIS_fnc_objectHeight",
        "BIS_fnc_objectSide",
        "BIS_fnc_objectType",
        "BIS_fnc_packStaticCamera",
        "BIS_fnc_param",
        "BIS_fnc_position",
        "BIS_fnc_prepareRespawnMenuInventory",
        "BIS_fnc_rankParams",
        "BIS_fnc_removeRespawnPosition",
        "BIS_fnc_respawnMarker",
        "BIS_fnc_respawnMenuFinish",
        "BIS_fnc_respawnSleep",
        "BIS_fnc_returnChildren",
        "BIS_fnc_returnNestedElement",
        "BIS_fnc_reverseList",
        "BIS_fnc_rotorsMap",
        "BIS_fnc_safeZoneCoef",
        "BIS_fnc_say2",
        "BIS_fnc_say3D",
        "BIS_fnc_sceneCamp",
        "BIS_fnc_sel",
        "BIS_fnc_selectRandom",
        "BIS_fnc_selectRandomWeighted",
        "BIS_fnc_setHeight",
        "BIS_fnc_setSkill",
        "BIS_fnc_shake",
        "BIS_fnc_showSubtitle",
        "BIS_fnc_sideChat",
        "BIS_fnc_sortAlphabetically",
        "BIS_fnc_sortBy",
        "BIS_fnc_sortNum",
        "BIS_fnc_spawnCrew",
        "BIS_fnc_spawnEnemy",
        "BIS_fnc_spawnGroup",
        "BIS_fnc_spawnVehicle",
        "BIS_fnc_stringToRGB",
        "BIS_fnc_switchLamp",
        "BIS_fnc_taskChildren",
        "BIS_fnc_taskCreate",
        "BIS_fnc_taskCurrent",
        "BIS_fnc_taskDescription",
        "BIS_fnc_taskDestination",
        "BIS_fnc_taskHint",
        "BIS_fnc_taskMarker",
        "BIS_fnc_taskReal",
        "BIS_fnc_taskSetCurrent",
        "BIS_fnc_taskState",
        "BIS_fnc_taskType",
        "BIS_fnc_teamColor",
        "BIS_fnc_textt",
        "BIS_fnc_TRACE",
        "BIS_fnc_trace",
        "BIS_fnc_transformDate",
        "BIS_fnc_typeOf",
        "BIS_fnc_unitHeadgear",
        "BIS_fnc_unitPlay",
        "BIS_fnc_UnitPos",
        "BIS_fnc_vectorMultiply",
        "BIS_fnc_weaponAdd",
        "BIS_fnc_weaponComponents",
        "BIS_fnc_weaponName",
        "BIS_fnc_weaponState",
        "BIS_fnc_wp",
        #endregion
    };

    // Event handler names (for displaySetEventHandler / addEventHandler etc.)
    private static readonly HashSet<string> KnownEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        // Unit / Damage
        "Dammaged",
        "Deleted",
        "Explosion",
        "Fired",
        "FiredLauncher",
        "FiredMan",
        "FiredNear",
        "HandleDamage",
        "HandleHeal",
        "Hit",
        "HitPart",
        "Killed",
        "Killer",
        "Registered",
        "Respawn",
        "Unregistered",

        // Vehicle / Transport
        "Eject",
        "Engine",
        "EpeContactEnd",
        "EpeContactStart",
        "Fuel",
        "FuelChanged",
        "GetIn",
        "GetOut",
        "LandedStopped",
        "LandedTouchDown",
        "RopeAttach",
        "RopeBreak",
        "RopeLengthChange",
        "SeatSwitch",
        "SeatSwitched",

        // Weapon / Reload
        "Reloaded",
        "Reloading",
        "WeaponAssembled",
        "WeaponDisassembled",

        // Inventory
        "ContainerClosed",
        "ContainerOpened",
        "InventoryClosed",
        "InventoryOpened",
        "ItemAssembled",
        "ItemDisassembled",
        "Put",
        "Take",
        "TurnIn",
        "TurnOut",

        // Animation
        "AnimChanged",
        "AnimDone",
        "AnimStateChanged",
        "GestureChanged",
        "GestureDone",

        // Vision / Optics
        "OpticsModeChanged",
        "PlayerViewChanged",
        "VisionModeChanged",

        // Suppression
        "Suppressed",
        "SuppressionChanged",

        // Mission / System
        "ControlsShifted",
        "Draw3D",
        "EachFrame",
        "Local",
        "Map",
        "MapSingleClick",
        "MissionLoaded",
        "PostReset",
        "Preload",
        "PreloadFinished",
        "Remote",
        "TaskSetAsCurrent",

        // Music
        "MusicPlayed",
        "MusicStart",
        "MusicStop",

        // Curator / Zeus
        "CuratorFeedbackMessage",
        "CuratorGroupPlaced",
        "CuratorGroupSelectionChanged",
        "CuratorMarkerPlaced",
        "CuratorMarkerSelectionChanged",
        "CuratorObjectDeleted",
        "CuratorObjectEdited",
        "CuratorObjectPlaced",
        "CuratorObjectRegistered",
        "CuratorObjectSelectionChanged",
        "CuratorObjectUnregistered",
        "CuratorPinged",
        "CuratorSelectionsChanged",
        "CuratorWaypointDeleted",
        "CuratorWaypointPlaced",
        "CuratorWaypointSelectionChanged",

        // GUI / Display Events
        "onButtonClick",
        "onChar",
        "onChildDestroy",
        "onFocus",
        "onKeyDown",
        "onKeyUp",
        "onKillFocus",
        "onLBAdd",
        "onLBDblClick",
        "onLBSelChanged",
        "onLoad",
        "onMouseButtonClick",
        "onMouseButtonDblClick",
        "onMouseButtonDown",
        "onMouseButtonUp",
        "onMouseEnter",
        "onMouseExit",
        "onMouseMoving",
        "onMouseZChange",
        "onObjectMoved",
        "onObjectRotated",
        "onObjectSelected",
        "onSetFocus",
        "onSliderPosChanged",
        "onTreeCollapsed",
        "onTreeDblClick",
        "onTreeExpanded",
        "onTreeSelChanged",
        "onTVSelChanged",
        "onUnfocus",
        "onUnload",
    };

    // Commands that are commonly used as prefix-unary (take one argument before them)
    private static readonly HashSet<string> UnaryCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        // Type / Null checks
        "isEqualTo",
        "isEqualType",
        "isEqualTypeAll",
        "isNil",
        "isNotEqualTo",
        "isNull",
        "typeName",
        "typeof",
        "typeOf",

        // Object checks
        "alive",
        "canMove",
        "canStand",
        "captive",
        "engineOn",
        "hasInterface",
        "isAutotest",
        "isDedicated",
        "isEngineOn",
        "isHidden",
        "isLightOn",
        "isMultiplayer",
        "isObjectHidden",
        "isObjectZoomable",
        "isPlayer",
        "isServer",
        "local",
        "locked",
        "objectParent",
        "vehicle",

        // Math
        "abs",
        "acos",
        "asin",
        "atan",
        "ceil",
        "cos",
        "deg",
        "exp",
        "floor",
        "ln",
        "log",
        "max",
        "min",
        "parseFloat",
        "parseNumber",
        "pi",
        "rad",
        "round",
        "sin",
        "sqrt",
        "tan",

        // String conversion
        "str",
        "toArray",
        "toFixed",
        "toLower",
        "toNumber",
        "toString",
        "toUpper",

        // Array
        "count",
        "findIf",
        "select",

        // Output
        "diag_fps",
        "diag_fpsMin",
        "diag_log",
        "diag_tickTime",
        "hint",
        "hintC",
        "hintSilent",
        "systemChat",

        // Unit lists
        "allDead",
        "allDeadMen",
        "allGroups",
        "allMissionObjects",
        "allPlayers",
        "allSimpleObjects",
        "allUnits",
        "vehicles",

        // World
        "clientOwner",
        "date",
        "daytime",
        "difficulty",
        "fog",
        "gameVersion",
        "humidity",
        "lightnings",
        "missionDifficulty",
        "missionName",
        "missionVersion",
        "moonIntensity",
        "overcast",
        "productVersion",
        "rain",
        "serverName",
        "serverTime",
        "sunOrMoon",
        "time",
        "waves",
        "wind",
        "worldName",
        "worldSize",

        // Unit info
        "boundingBox",
        "boundingCenter",
        "damage",
        "faction",
        "fuel",
        "getUnitLoadout",
        "player",
        "rank",
        "rating",
        "side",
        "sizeOf",
        "skill",

        // Inventory
        "assignedItems",
        "backpackItems",
        "currentMagazine",
        "currentMuzzle",
        "currentVisionMode",
        "currentWeapon",
        "handgunWeapon",
        "items",
        "magazines",
        "primaryWeapon",
        "secondaryWeapon",
        "uniformItems",
        "vestItems",
        "weaponDirection",
        "weapons",
        "weaponState",

        // Position
        "aimPos",
        "distance",
        "distance2D",
        "eyeDirection",
        "eyePos",
        "getPos",
        "getPosASL",
        "getPosASLW",
        "getPosATL",
        "getPosWorld",
        "mousePosition",
        "position",
        "visiblePosition",
        "visiblePositionASL",

        // Marker
        "getMarkerColor",
        "getMarkerPos",
        "markerAlpha",
        "markerBrush",
        "markerColor",
        "markerDir",
        "markerPos",
        "markerShape",
        "markerSize",
        "markerText",
        "markerType",

        // Config
        "configFile",
        "configName",
        "configOf",
        "getArray",
        "getNumber",
        "getText",
        "inheritsFrom",
        "isArray",
        "isClass",
        "isNumber",
        "isText",
        "missionConfigFile",

        // Spatial queries
        "nearestBuilding",
        "nearestLocation",
        "nearestLocations",
        "nearestObjects",
        "nearObjects",

        // GUI
        "ctrlClassName",
        "ctrlEnabled",
        "ctrlIDC",
        "ctrlParent",
        "ctrlPosition",
        "ctrlText",
        "ctrlType",
        "ctrlVisible",
        "findDialog",
        "findDisplay",
        "lbCurSel",
        "lbData",
        "lbSize",
        "lbText",
        "lbValue",
        "tvCount",
        "tvCurSel",
        "tvData",
        "tvText",
        "tvValue",

        // Object
        "commander",
        "crew",
        "driver",
        "effectiveCommander",
        "group",
        "gunner",
        "leader",
        "owner",
        "units",

        // Animation
        "animationPhase",
        "animationState",
    };

    // Variables that are built-in and should not be reassigned
    private static readonly HashSet<string> ReservedVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "_this", "_x", "_forEachIndex", "_thisFSM",
    };

    public IReadOnlyList<SqfLintDiagnostic> Diagnostics => _diagnostics;

    /// <summary>Apply auto-fixes to source text. Fixes are applied bottom-up to preserve offsets.</summary>
    public static string ApplyFixes(string sourceText, IReadOnlyList<SqfLintDiagnostic> diagnostics)
    {
        var fixes = diagnostics
            .Where(d => d.Fix != null)
            .Select(d => d.Fix!)
            .OrderByDescending(f => f.Offset)
            .ToList();

        if (fixes.Count == 0)
            return sourceText;

        var result = sourceText;
        foreach (var fix in fixes)
        {
            if (fix.Offset < 0 || fix.Offset + fix.Length > result.Length)
                continue;
            result = result.Remove(fix.Offset, fix.Length).Insert(fix.Offset, fix.ReplacementText);
        }
        return result;
    }

    public IReadOnlyList<SqfLintDiagnostic> Lint(SqfFile file)
    {
        _diagnostics.Clear();
        _sourceText = file.SourceText ?? "";

        // L-S01: Tab characters in source
        if (!string.IsNullOrEmpty(_sourceText))
        {
            CheckTabCharacters(file);
            CheckIndentation(_sourceText, file.FilePath);
        }

        var symbolTable = new SymbolTable();
        VisitStatements(file.Statements, symbolTable);
        CheckUnusedVariables(symbolTable);
        return _diagnostics;
    }

    private void VisitStatements(List<SqfStatement> stmts, SymbolTable symbols)
    {
        foreach (var stmt in stmts)
        {
            VisitStatement(stmt, symbols);
        }
    }

    private void VisitStatementsInScope(List<SqfStatement> stmts, SymbolTable symbols)
    {
        var childScope = symbols.WithChildScope();
        VisitStatements(stmts, childScope);
        // L-S12: unused local variable
        CheckUnusedVariables(childScope);
    }

    private void CheckUnusedVariables(SymbolTable scope)
    {
        foreach (var (name, node) in scope.GetUnusedDeclarations())
        {
            if (!name.StartsWith("_"))
                continue; // global variables don't need private
            _diagnostics.Add(new SqfLintDiagnostic(
                "L-S12", SqfLintSeverity.Warning,
                $"Unused local variable '{name}'",
                node.File, node.Line, node.Column));
        }
        // L-S13: unused parameter
        foreach (var (name, node) in scope.GetUnusedParams())
        {
            if (!name.StartsWith("_"))
                continue;
            _diagnostics.Add(new SqfLintDiagnostic(
                "L-S13", SqfLintSeverity.Warning,
                $"Unused parameter '{name}'",
                node.File, node.Line, node.Column));
        }
        // L-S15: unused assignment
        foreach (var (name, node) in scope.GetUnreadAssignments())
        {
            if (!name.StartsWith("_"))
                continue;
            _diagnostics.Add(new SqfLintDiagnostic(
                "L-S15", SqfLintSeverity.Warning,
                $"Unused assignment to '{name}'",
                node.File, node.Line, node.Column));
        }
    }

    private void VisitStatement(SqfStatement stmt, SymbolTable symbols)
    {
        switch (stmt)
        {
            case SqfExpressionStatement es:
                VisitExpression(es.Expression, symbols);
                break;

            case SqfIfStatement ifs:
                VisitExpression(ifs.Condition, symbols);
                VisitStatementsInScope(ifs.ThenBlock.Statements, symbols);
                if (ifs.ElseBlock != null)
                    VisitStatementsInScope(ifs.ElseBlock.Statements, symbols);

                // L-S11: if_not_else — check for ! in condition
                CheckIfNotElse(ifs);

                // L-S21: invalid_comparisons
                CheckInvalidComparisons(ifs.Condition);

                // L-S05: assignment in condition
                CheckAssignmentInCondition(ifs.Condition);
                break;

            case SqfWhileStatement ws:
                VisitStatementsInScope(ws.Condition.Statements, symbols);
                VisitStatementsInScope(ws.Body.Statements, symbols);
                break;

            case SqfForStatement fs:
                VisitStatementsInScope(fs.Init.Statements, symbols);
                VisitStatementsInScope(fs.Condition.Statements, symbols);
                VisitStatements(fs.Step.Statements, symbols);
                VisitStatementsInScope(fs.Body.Statements, symbols);
                break;

            case SqfForEachStatement fes:
                VisitExpression(fes.Collection, symbols);
                VisitStatementsInScope(fes.Body.Statements, symbols);
                break;

            case SqfSwitchStatement ss:
                VisitExpression(ss.Value, symbols);
                VisitStatementsInScope(ss.Body.Statements, symbols);
                break;

            case SqfPrivateStatement ps:
                foreach (var v in ps.Variables)
                {
                    string? varName = null;
                    SqfAstNode? nameNode = null;
                    if (v is SqfIdentifier id)
                    {
                        varName = id.Name;
                        nameNode = id;
                    }
                    else if (v is SqfStringLiteral sl)
                    {
                        // private "_x" — string literal variable name
                        varName = sl.Value;
                        nameNode = sl;
                    }
                    else if (v is SqfAssign assign)
                    {
                        // private _x = 1 — assignment with initialization
                        varName = assign.Name;
                        nameNode = assign;
                    }

                    if (varName != null && nameNode != null)
                    {
                        // L-S36: global_var_in_local
                        if (!varName.StartsWith("_"))
                        {
                            SqfLintFix? fix = null;
                            // Fix only applies to bare identifier form: private MyVar;
                            if (v is SqfIdentifier)
                            {
                                var varOff = GetOffset(nameNode.Line, nameNode.Column);
                                var privOff = _sourceText.LastIndexOf("private", varOff, StringComparison.OrdinalIgnoreCase);
                                if (privOff >= 0)
                                    fix = new SqfLintFix(privOff, varOff - privOff, "");
                            }
                            _diagnostics.Add(new SqfLintDiagnostic(
                                "L-S36", SqfLintSeverity.Error,
                                $"Global variable in private declaration: '{varName}'",
                                nameNode.File, nameNode.Line, nameNode.Column) { Fix = fix });
                        }
                        // L-S14: shadowing
                        if (symbols.IsShadowing(varName))
                        {
                            _diagnostics.Add(new SqfLintDiagnostic(
                                "L-S14", SqfLintSeverity.Warning,
                                $"Variable '{varName}' shadows existing variable from outer scope",
                                nameNode.File, nameNode.Line, nameNode.Column));
                        }
                        symbols.Declare(varName, stmt);
                    }
                }
                break;

            case SqfParamsStatement pms:
                foreach (var p in pms.Parameters)
                {
                    string? paramName = null;
                    SqfAstNode? nameNode = null;

                    if (p is SqfIdentifier id)
                    {
                        paramName = id.Name;
                        nameNode = id;
                    }
                    else if (p is SqfStringLiteral strLit)
                    {
                        // params ["_a", "_b"] — string literal param names
                        paramName = strLit.Value;
                        nameNode = strLit;
                    }
                    // params can have defaults: ["_var", defaultValue]
                    else if (p is SqfArrayLiteral arr && arr.Elements.Count > 0)
                    {
                        if (arr.Elements[0] is SqfIdentifier arrId)
                        {
                            paramName = arrId.Name;
                            nameNode = arrId;
                        }
                        else if (arr.Elements[0] is SqfStringLiteral arrStr)
                        {
                            paramName = arrStr.Value;
                            nameNode = arrStr;
                        }
                    }

                    if (paramName != null && nameNode != null)
                    {
                        if (!paramName.StartsWith("_"))
                        {
                            _diagnostics.Add(new SqfLintDiagnostic(
                                "L-S36", SqfLintSeverity.Error,
                                $"Global variable in params declaration: '{paramName}'",
                                nameNode.File, nameNode.Line, nameNode.Column));
                        }
                        if (symbols.IsShadowing(paramName))
                        {
                            _diagnostics.Add(new SqfLintDiagnostic(
                                "L-S14", SqfLintSeverity.Warning,
                                $"Variable '{paramName}' shadows existing variable from outer scope",
                                nameNode.File, nameNode.Line, nameNode.Column));
                        }
                        symbols.Declare(paramName, stmt, isParam: true);
                    }
                }
                break;

            default:
                // Unknown statement type — skip
                break;
        }
    }

    private void VisitExpression(SqfExpression expr, SymbolTable symbols)
    {
        switch (expr)
        {
            case SqfIdentifier id:
                // Track identifier usage for L-S12/L-S13
                symbols.Use(id.Name, expr);
                // L-S15: reading a variable clears pending unread assignments
                if (id.Name.StartsWith("_"))
                    symbols.MarkAssignmentRead(id.Name);
                // L-S04: command_case — only check if it looks like a command (not _var)
                if (!id.Name.StartsWith("_") && KnownCommands.Contains(id.Name))
                {
                    // Check if the identifier matches known casing
                    if (KnownCommands.TryGetValue(id.Name, out var correctCase) && id.Name != correctCase)
                    {
                        var off = GetOffset(id.Line, id.Column);
                        var fix = new SqfLintFix(off, id.Name.Length, correctCase);
                        _diagnostics.Add(new SqfLintDiagnostic(
                            "L-S04", SqfLintSeverity.Help,
                            $"Command '{id.Name}' should be '{correctCase}'",
                            id.File, id.Line, id.Column) { Fix = fix });
                    }
                }
                // L-S17: var_all_caps
                if (!id.Name.StartsWith("_") && id.Name == id.Name.ToUpperInvariant() && id.Name.Length > 1)
                {
                    var off = GetOffset(id.Line, id.Column);
                    var lower = id.Name.ToLowerInvariant();
                    var fix = new SqfLintFix(off, id.Name.Length, lower);
                    _diagnostics.Add(new SqfLintDiagnostic(
                        "L-S17", SqfLintSeverity.Warning,
                        $"Variable '{id.Name}' is all-caps — may be an undefined macro",
                        id.File, id.Line, id.Column) { Fix = fix });
                }
                // L-S23: reassign_reserved_variable — handled in assignments
                break;

            case SqfNumberLiteral nl:
                CheckMagicNumber(nl);
                break;

            case SqfBooleanLiteral:
            case SqfStringLiteral:
            case SqfStringExpr:
                break;

            case SqfArrayLiteral arr:
                foreach (var el in arr.Elements)
                    VisitExpression(el, symbols);
                break;

            case SqfCodeBlock block:
                VisitStatementsInScope(block.Statements, symbols);
                break;

            case SqfParenExpr pe:
                VisitExpression(pe.Inner, symbols);
                break;

            case SqfConfigPath cp:
                foreach (var part in cp.Parts)
                    VisitExpression(part, symbols);
                break;

            case SqfUnaryOp uo:
                VisitExpression(uo.Operand, symbols);
                // L-S19: extra_not
                if (uo.Operator == "!")
                {
                    var inner = uo.Operand;
                    while (inner is SqfParenExpr paren)
                        inner = paren.Inner;
                    if (inner is SqfBinaryOp innerBin && innerBin.Operator == "isEqualTo")
                    {
                        var off = GetOffset(uo.Line, uo.Column);
                        var fix = new SqfLintFix(off, 1, "");
                        _diagnostics.Add(new SqfLintDiagnostic(
                            "L-S19", SqfLintSeverity.Help,
                            $"Extra '!' before comparison — use 'isNotEqualTo' instead",
                            uo.File, uo.Line, uo.Column) { Fix = fix });
                    }
                }
                break;

            case SqfBinaryOp bin:
                VisitExpression(bin.Left, symbols);
                VisitExpression(bin.Right, symbols);

                // L-S05: if_assign — detect if used as value in binary
                if (bin.Operator is "==" or ">" or "<" or ">=" or "<=" or "!=")
                {
                    // This is just a generic comparison check
                }

                // L-S20: bool_static_comparison
                if (bin.Operator == "==" || bin.Operator == "isEqualTo")
                {
                    if (bin.Right is SqfBooleanLiteral bl)
                    {
                        SqfLintFix? fix = null;
                        if (bl.Value)
                        {
                            // _x == true → _x: remove " == true" from source
                            // Note: bin.Line/Col points to left-most token, not operator.
                            // Search for == between left and right operands.
                            var leftOff = GetOffset(bin.Left.Line, bin.Left.Column);
                            var rightOff = GetOffset(bin.Right.Line, bin.Right.Column);
                            var eqOff = _sourceText.IndexOf("==", leftOff, rightOff - leftOff);
                            if (eqOff >= 0)
                            {
                                var start = eqOff;
                                while (start > 0 && (_sourceText[start - 1] == ' ' || _sourceText[start - 1] == '\t'))
                                    start--;
                                var end = rightOff + 4;
                                while (end < _sourceText.Length && (_sourceText[end] == ' ' || _sourceText[end] == '\t'))
                                    end++;
                                fix = new SqfLintFix(start, end - start, "");
                            }
                        }
                        _diagnostics.Add(new SqfLintDiagnostic(
                            "L-S20", SqfLintSeverity.Warning,
                            $"Comparison to boolean literal — '{bin.Left}' can be used directly",
                            bin.File, bin.Line, bin.Column) { Fix = fix });
                    }
                }

                // L-S06: find_in_str — detect `find ... > -1`
                if (bin.Operator == ">" && bin.Right is SqfNumberLiteral nr && nr.Text == "-1")
                {
                    if (bin.Left is SqfBinaryOp findOp && findOp.Operator == "find")
                    {
                        _diagnostics.Add(new SqfLintDiagnostic(
                            "L-S06", SqfLintSeverity.Warning,
                            $"Use 'in' instead of 'find ... > -1'",
                            bin.File, bin.Line, bin.Column));
                    }
                }

                // L-S18: in_vehicle_check
                if (bin.Operator == "==" && IsVehicleEqualExpr(bin.Left, bin.Right))
                {
                    _diagnostics.Add(new SqfLintDiagnostic(
                        "L-S18", SqfLintSeverity.Warning,
                        $"Use 'isNull objectParent X' instead of 'vehicle X == X'",
                        bin.File, bin.Line, bin.Column));
                }

                // L-S25: count_array_comp
                // Parser produces SqfCall for commands (not SqfUnaryOp)
                if (bin.Operator == "==" && bin.Right is SqfNumberLiteral nrc && nrc.Text == "0")
                {
                    SqfExpression? countTarget = null;
                    if (bin.Left is SqfUnaryOp uo2 && uo2.Operator == "count")
                        countTarget = uo2.Operand;
                    else if (bin.Left is SqfCall countCall &&
                             string.Equals((countCall.Target as SqfIdentifier)?.Name, "count", StringComparison.OrdinalIgnoreCase))
                        countTarget = countCall.Arguments.FirstOrDefault();
                    if (countTarget is SqfIdentifier countArr)
                    {
                        var startOff = GetOffset(bin.Line, bin.Column);
                        var endOff = GetOffset(nrc.Line, nrc.Column) + nrc.Text.Length;
                        var fix = new SqfLintFix(startOff, endOff - startOff, $"{countArr.Name} isEqualTo []");
                        _diagnostics.Add(new SqfLintDiagnostic(
                            "L-S25", SqfLintSeverity.Help,
                            $"Use '{countArr.Name} isEqualTo []' instead of 'count {countArr.Name} == 0'",
                            bin.File, bin.Line, bin.Column) { Fix = fix });
                    }
                }

                // L-S27: select_count
                if (bin.Operator is "select" or "select")
                {
                    CheckSelectCount(bin);
                }
                break;

            case SqfAssign assign:
                // L-S16: not_private
                if (assign.Name.StartsWith("_") && !symbols.IsDeclared(assign.Name))
                {
                    var off = GetOffset(assign.Line, assign.Column);
                    var fix = new SqfLintFix(off, 0, "private ");
                    _diagnostics.Add(new SqfLintDiagnostic(
                        "L-S16", SqfLintSeverity.Help,
                        $"Local variable '{assign.Name}' should be private",
                        assign.File, assign.Line, assign.Column) { Fix = fix });
                }
                // L-S14: shadowing check for assignments
                if (symbols.IsShadowing(assign.Name))
                {
                    _diagnostics.Add(new SqfLintDiagnostic(
                        "L-S14", SqfLintSeverity.Warning,
                        $"Variable '{assign.Name}' shadows existing variable from outer scope",
                        assign.File, assign.Line, assign.Column));
                }
                // L-S23: reassign_reserved_variable
                if (ReservedVariables.Contains(assign.Name))
                {
                    _diagnostics.Add(new SqfLintDiagnostic(
                        "L-S23", SqfLintSeverity.Error,
                        $"Cannot reassign reserved variable '{assign.Name}'",
                        assign.File, assign.Line, assign.Column));
                }
                // Visit RHS first so reads of the assigned var clear prior unread assignments
                VisitExpression(assign.Value, symbols);
                // L-S15: unused_assignment — prior unread assignments to this variable are dead
                if (assign.Name.StartsWith("_"))
                {
                    var deadAssignments = symbols.DrainUnreadAssignments(assign.Name);
                    foreach (var dead in deadAssignments)
                    {
                        _diagnostics.Add(new SqfLintDiagnostic(
                            "L-S15", SqfLintSeverity.Warning,
                            $"Unused assignment to '{assign.Name}'",
                            dead.File, dead.Line, dead.Column));
                    }
                    symbols.TrackAssignment(assign.Name, assign);
                }
                symbols.Declare(assign.Name, expr);
                break;

            case SqfArrayAccess aa:
                VisitExpression(aa.Array, symbols);
                VisitExpression(aa.Index, symbols);
                break;

            case SqfCall call:
                if (call.Target is SqfIdentifier callTarget)
                {
                    // L-S27: select (count _array - 1) → select -1
                    if (string.Equals(callTarget.Name, "select", StringComparison.OrdinalIgnoreCase) &&
                        call.Arguments.Count == 2)
                    {
                        CheckSelectCountSqfCall(call.Arguments[0], call.Arguments[1], callTarget);
                    }
                }
                VisitExpression(call.Target, symbols);
                foreach (var arg in call.Arguments)
                    VisitExpression(arg, symbols);
                break;
        }
    }

    private static bool IsVehicleEqualExpr(SqfExpression left, SqfExpression right)
    {
        // Pattern: vehicle X == X
        if (left is SqfUnaryOp uo && uo.Operator == "vehicle")
        {
            // Check if right side is the same expression as the operand
            return AreSameExpr(uo.Operand, right);
        }
        return false;
    }

    private static bool AreSameExpr(SqfExpression a, SqfExpression b)
    {
        return a switch
        {
            SqfIdentifier idA => b is SqfIdentifier idB && idA.Name == idB.Name,
            _ => a.ToString() == b.ToString(),
        };
    }

    private void CheckSelectCount(SqfBinaryOp bin)
    {
        // Pattern: _array select (count _array - 1)  →  _array select -1
        if (bin.Right is SqfBinaryOp subOp &&
            subOp.Operator == "-" &&
            subOp.Right is SqfNumberLiteral one &&
            one.Text == "1" &&
            subOp.Left is SqfUnaryOp countOp &&
            countOp.Operator == "count" &&
            countOp.Operand is SqfIdentifier countArr &&
            bin.Left is SqfIdentifier selectArr &&
            countArr.Name == selectArr.Name)
        {
            _diagnostics.Add(new SqfLintDiagnostic(
                "L-S27", SqfLintSeverity.Help,
                $"Use 'select -1' instead of 'select (count _array - 1)'",
                bin.File, bin.Line, bin.Column));
        }
    }

    private void CheckSelectCountSqfCall(SqfExpression arrayExpr, SqfExpression indexExpr, SqfIdentifier selectId)
    {
        // Pattern: _array select (count _array - 1)  ->  _array select -1
        // AST: SqfCall("select", [SqfIdentifier("_array"), SqfParenExpr(SqfBinaryOp("-", ..., Num("1")))])

        // Verify index is (count _array - 1)
        if (indexExpr is SqfParenExpr paren &&
            paren.Inner is SqfBinaryOp subOp &&
            subOp.Operator == "-" &&
            subOp.Right is SqfNumberLiteral one &&
            one.Text == "1" &&
            arrayExpr is SqfIdentifier arrayId)
        {
            // The parser produces SqfCall("_array", [SqfIdentifier("count")]) for "count _array"
            // Check: left side of "-" involves count call on same array
            if (subOp.Left is SqfCall countCall &&
                countCall.Target is SqfIdentifier countTarget &&
                string.Equals(countTarget.Name, arrayId.Name, StringComparison.OrdinalIgnoreCase) &&
                countCall.Arguments.Any(a => a is SqfIdentifier ci &&
                    string.Equals(ci.Name, "count", StringComparison.OrdinalIgnoreCase)))
            {
                // Find (count array - 1) in source, replace with -1
                // SqfParenExpr position may not align with `(`, so search source text
                var selectOff = GetOffset(selectId.Line, selectId.Column);
                var parenOpen = _sourceText.IndexOf('(', selectOff);
                var parenClose = parenOpen >= 0 ? _sourceText.IndexOf(')', parenOpen) : -1;
                SqfLintFix? fix = null;
                if (parenClose >= 0)
                    fix = new SqfLintFix(parenOpen, parenClose - parenOpen + 1, "-1");
                _diagnostics.Add(new SqfLintDiagnostic(
                    "L-S27", SqfLintSeverity.Help,
                    $"Use '{arrayId.Name} select -1' instead of '{arrayId.Name} select (count {arrayId.Name} - 1)'",
                    selectId.File, selectId.Line, selectId.Column) { Fix = fix });
            }
        }
    }

    private void CheckIfNotElse(SqfIfStatement ifs)
    {
        // L-S11: if (!cond) then { a } else { b } → if (cond) then { b } else { a }
        if (ifs.Condition is SqfUnaryOp notOp && notOp.Operator == "!")
        {
            _diagnostics.Add(new SqfLintDiagnostic(
                "L-S11", SqfLintSeverity.Help,
                $"Unneeded '!' in condition — swap if/else blocks",
                ifs.Condition.File, ifs.Condition.Line, ifs.Condition.Column));
        }
    }

    private void CheckAssignmentInCondition(SqfExpression cond)
    {
        // L-S05: detect = used instead of == in if/while/for conditions
        if (cond is SqfAssign assign)
        {
            // SqfAssign line/col points to the identifier, not the = operator.
            // Find the = in source text after the variable name.
            var startOffset = GetOffset(assign.Line, assign.Column);
            var eqOffset = _sourceText.IndexOf('=', startOffset + assign.Name.Length);
            SqfLintFix? fix = null;
            if (eqOffset >= 0)
                fix = new SqfLintFix(eqOffset, 1, "==");
            _diagnostics.Add(new SqfLintDiagnostic(
                "L-S05", SqfLintSeverity.Error,
                $"Assignment in condition — use '==' instead of '='",
                assign.File, assign.Line, assign.Column) { Fix = fix });
        }
    }

    private void CheckTabCharacters(SqfFile file)
    {
        // L-S01: Detect tab characters in source
        var text = file.SourceText;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\t')
            {
                var line = 1;
                var col = 1;
                for (int j = 0; j < i; j++)
                {
                    if (text[j] == '\n') { line++; col = 1; }
                    else col++;
                }
                var fix = new SqfLintFix(i, 1, "    ");
                _diagnostics.Add(new SqfLintDiagnostic(
                    "L-S01", SqfLintSeverity.Warning,
                    "Tab character detected — use spaces for indentation",
                    file.FilePath, line, col) { Fix = fix });
                // Only report first tab per line to reduce noise
                var nextLine = text.IndexOf('\n', i);
                if (nextLine < 0) break;
                i = nextLine;
            }
        }
    }

    private void CheckIndentation(string sourceText, string filePath)
    {
        // L-S02: Detect inconsistent indentation
        var lines = sourceText.Split('\n');
        int tabLines = 0, spaceLines = 0;

        // First pass: detect lines mixing tabs+spaces, count tab/space lines
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            bool hasLeadingTab = false, hasLeadingSpace = false;
            foreach (char c in line)
            {
                if (c == '\t') hasLeadingTab = true;
                else if (c == ' ') hasLeadingSpace = true;
                else break;
            }

            if (!hasLeadingTab && !hasLeadingSpace)
                continue;

            if (hasLeadingTab && hasLeadingSpace)
            {
                _diagnostics.Add(new SqfLintDiagnostic(
                    "L-S02", SqfLintSeverity.Warning,
                    "Line mixes tabs and spaces for indentation",
                    filePath, i + 1, 1));
            }
            else if (hasLeadingTab)
                tabLines++;
            else
                spaceLines++;
        }

        // Second pass: if file uses both tab and space indentation, flag minority
        if (tabLines > 0 && spaceLines > 0)
        {
            bool dominantIsTab = tabLines >= spaceLines;
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                bool hasLeadingTab = false, hasLeadingSpace = false;
                foreach (char c in line)
                {
                    if (c == '\t') hasLeadingTab = true;
                    else if (c == ' ') hasLeadingSpace = true;
                    else break;
                }

                if (!hasLeadingTab && !hasLeadingSpace)
                    continue;
                if (hasLeadingTab && hasLeadingSpace)
                    continue; // already warned above

                if (dominantIsTab && hasLeadingSpace)
                {
                    _diagnostics.Add(new SqfLintDiagnostic(
                        "L-S02", SqfLintSeverity.Error,
                        "Inconsistent indentation — file uses tabs, this line uses spaces",
                        filePath, i + 1, 1));
                }
                else if (!dominantIsTab && hasLeadingTab)
                {
                    _diagnostics.Add(new SqfLintDiagnostic(
                        "L-S02", SqfLintSeverity.Error,
                        "Inconsistent indentation — file uses spaces, this line uses tabs",
                        filePath, i + 1, 1));
                }
            }
        }
    }

    /// <summary>Get 0-based character offset from 1-based line/col in source text.</summary>
    private int GetOffset(int line, int col)
    {
        int curLine = 1;
        for (int i = 0; i < _sourceText.Length; i++)
        {
            if (curLine == line)
                return i + col - 1;
            if (_sourceText[i] == '\n')
                curLine++;
        }
        return _sourceText.Length;
    }

    private static readonly HashSet<string> AllowedNumbers = new()
    {
        "0", "1", "-1", "100", "1000", "255",
    };

    private void CheckMagicNumber(SqfNumberLiteral nl)
    {
        if (nl.Text.StartsWith("$"))
            return;
        if (AllowedNumbers.Contains(nl.Text))
            return;
        _diagnostics.Add(new SqfLintDiagnostic(
            "L-S24", SqfLintSeverity.Help,
            $"Magic number '{nl.Text}' — consider using a named constant",
            nl.File, nl.Line, nl.Column));
    }

    private void CheckInvalidComparisons(SqfExpression cond)
    {
        // L-S21: detect impossible conditions like _x < 20 && _x > 30
        if (cond is SqfBinaryOp andOp && andOp.Operator == "&&")
        {
            if (andOp.Left is SqfBinaryOp left && andOp.Right is SqfBinaryOp right)
            {
                if (left.Left is SqfIdentifier leftId && right.Left is SqfIdentifier rightId &&
                    leftId.Name == rightId.Name)
                {
                    if (left.Operator == "<" && right.Operator == ">" &&
                        left.Right is SqfNumberLiteral ln && right.Right is SqfNumberLiteral rn)
                    {
                        if (double.TryParse(ln.Text, out var lv) && double.TryParse(rn.Text, out var rv) && lv <= rv)
                        {
                            _diagnostics.Add(new SqfLintDiagnostic(
                                "L-S21", SqfLintSeverity.Error,
                                $"Impossible condition: '{leftId.Name} < {lv} && {leftId.Name} > {rv}'",
                                cond.File, cond.Line, cond.Column));
                        }
                    }
                }
            }
        }
    }
}

/// <summary>Simple symbol table for variable tracking within scopes.</summary>
internal class SymbolTable
{
    private readonly SymbolTable? _parent;
    private readonly Dictionary<string, SqfAstNode> _declared = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _params = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<SqfAstNode>> _unreadAssignments = new(StringComparer.OrdinalIgnoreCase);

    public SymbolTable(SymbolTable? parent = null)
    {
        _parent = parent;
    }

    public void Declare(string name, SqfAstNode node, bool isParam = false)
    {
        _declared[name] = node;
        if (isParam)
            _params.Add(name);
    }

    /// <summary>Returns true if the name already exists in a PARENT scope (shadowing).</summary>
    public bool IsShadowing(string name)
    {
        return _parent != null && _parent.IsDeclared(name);
    }

    public void Use(string name, SqfAstNode node)
    {
        _used.Add(name);
        _parent?.Use(name, node);
    }

    public bool IsDeclared(string name)
    {
        return _declared.ContainsKey(name) || (_parent?.IsDeclared(name) ?? false);
    }

    public IEnumerable<(string Name, SqfAstNode Node)> GetUnusedDeclarations()
    {
        foreach (var kv in _declared)
        {
            if (!_used.Contains(kv.Key) && !_params.Contains(kv.Key))
                yield return (kv.Key, kv.Value);
        }
    }

    public IEnumerable<(string Name, SqfAstNode Node)> GetUnusedParams()
    {
        foreach (var kv in _declared)
        {
            if (!_used.Contains(kv.Key) && _params.Contains(kv.Key))
                yield return (kv.Key, kv.Value);
        }
    }

    /// <summary>Track an assignment that hasn't been read yet (for L-S15).</summary>
    public void TrackAssignment(string name, SqfAstNode node)
    {
        if (!_unreadAssignments.TryGetValue(name, out var list))
        {
            list = new List<SqfAstNode>();
            _unreadAssignments[name] = list;
        }
        list.Add(node);
    }

    /// <summary>Mark all pending assignments for a variable as read (variable was used).</summary>
    public void MarkAssignmentRead(string name)
    {
        _unreadAssignments.Remove(name);
        _parent?.MarkAssignmentRead(name);
    }

    /// <summary>Drain (return and clear) all unread assignments for a variable across scope chain.</summary>
    public List<SqfAstNode> DrainUnreadAssignments(string name)
    {
        var result = new List<SqfAstNode>();
        if (_unreadAssignments.TryGetValue(name, out var nodes))
        {
            result.AddRange(nodes);
            _unreadAssignments.Remove(name);
        }
        if (_parent != null)
            result.AddRange(_parent.DrainUnreadAssignments(name));
        return result;
    }

    /// <summary>Get all unread assignments in this scope (for scope-exit check).</summary>
    public IEnumerable<(string Name, SqfAstNode Node)> GetUnreadAssignments()
    {
        foreach (var kv in _unreadAssignments)
            foreach (var node in kv.Value)
                yield return (kv.Key, node);
    }

    public SymbolTable WithChildScope()
    {
        return new SymbolTable(this);
    }
}
