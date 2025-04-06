# CS2 Rock The Vote
![Downloads](https://img.shields.io/github/downloads/Oz-Lin/cs2-rockthevote/total) ![Last commit](https://img.shields.io/github/last-commit/Oz-Lin/cs2-rockthevote "Last commit") ![Open issues](https://img.shields.io/github/issues/Oz-Lin/cs2-rockthevote "Open Issues") ![Closed issues](https://img.shields.io/github/issues-closed/Oz-Lin/cs2-rockthevote "Closed Issues") ![Size](https://img.shields.io/github/repo-size/abnerfs/dontpad-api "Size")

![image](https://github.com/Oz-Lin/cs2-rockthevote/blob/main/example_image.png)

General purpose map voting plugin, started as a simple RTV and now has more features

# Enjoying the plugin?
Please drop a ⭐ star in the repository 

> [!WARNING]
> Notes from Oz-Lin:
> 
> This fork of the plugin mainly considers ZE/ZM/ZR Zombie Escape/Survival/Riot, or any round-based game modes.
>
> Now testing for non-round-based modes such as bhop/surf/kz/mg-course! Make sure both `mp_timelimit` and `mp_roundtime` has the same value. 
>
> ~~For non-round-based modes such as bhop/surf/kz/mg-course, I cannot guarantee your use case.~~ Please double-check your server cvar configs (such as `mp_timelimit`, `mp_maxrounds` etc, along with `TriggerSecondsBeforeEnd` and `TriggerRoundsBeforeEnd` in config.json) in case of compatibility issues.
  
## Requirements
- [Latest release of Counter Strike Sharp](https://github.com/roflmuffin/CounterStrikeSharp)
- [ScreenMenuAPI](https://github.com/T3Marius/CS2ScreenMenuAPI) 

# Installation
- Download the latest release from https://github.com/Oz-Lin/cs2-rockthevote/releases
- Extract the .zip file into `addons/counterstrikesharp/plugins`
- Enjoy

# Features
- Reads from a custom maplist
- RTV/unrtv Command
- Timeleft command
- Nominate command (with partial map name matching [#31](https://github.com/abnerfs/cs2-rockthevote/pull/31))
- Votemap command
- Supports workshop maps
- Nextmap command
- !ext map time limit extension command
- Ignore players in Spectators from vote count
- Added "Extend current map" in end of map vote
- !revote during RTV or VIPextend vote to change the option
- [VIP flag] Vote to extend current map by time limit
- [Admin flag] Extend current map time limit
- Force RTV from the server side [#70](https://github.com/abnerfs/cs2-rockthevote/pull/70)
- Fully configurable
- Translated by the community
- Nomlist command to see who nominated which map
- Vote to extend current map by maximum rounds as well (Thanks Cruze03 [#3](https://github.com/Oz-Lin/cs2-rockthevote/pull/3))
- "Allow extends" checker and extend limits in RTV vote
- Compatibility for non-round-based game mode during map time extension

# Work in Progress
- Cooldown to start another RTV after the last vote
- Bug fix on "ignore specs" checker

# ⚠️ Help Wanted
- WASD menu - a lot of code refactoring must be done before implementing this feature. Can't give guarantee about this feature. 
- Currently work in progress. Thanks T3-Marius, XiaoLinWuDi, theextr1m

# Translations
| Language             | Contributor          |
| -------------------- | -------------------- |
| Brazilian Portuguese | abnerfs + ChatGPT             |
| English              | abnerfs + Oz-Lin             |
| Ukrainian            | panikajo + ChatGPT            |
| Turkish              | brkvlr + ChatGPT              |
| Russian              | Auttend + ChatGPT             |
| Latvian              | rcon420 + ChatGPT             |
| Hungarian            | Chickender, lovasatt + ChatGPT |
| Polish               | D3X + ChatGPT                 |
| French               | o3LL + ChatGPT                |
| Chinese (zh-Hans)    | himenekocn + Oz-Lin          |
| Chinese (zh-Hant)    | himenekocn + Oz-Lin          |
| Korean       | wjdrkfka3              |

# Configs
- A config file will be created in `addons/counterstrikesharp/configs/plugins/RockTheVote` the first time you load the plugin.
- Changes in the config file will require you to reload the plugin or restart the server (change the map won't work).
- Maps that will be used in RTV/nominate/votemap/end of map vote are located in `addons/counterstrikesharp/configs/plugins/RockTheVote/maplist.txt` (rename `maplist.example.txt` to `maplist.txt`)

## General config
| Config         | Description                                                                      | Default Value | Min | Max |
| -------------- | -------------------------------------------------------------------------------- | ------------- | --- | --- |
| MapsInCoolDown | Number of maps that can't be used in vote because they have been played recently | 3             | 0   |     |

## RockTheVote
Players can type rtv to request the map to be changed, once a number of votes is reached (by default 60% of players in the server) a vote will start for the next map, this vote lasts up to 30 seconds (hardcoded for now), in the end server changes to the winner map.

| Config              | Description                                                                                                            | Default Value | Min   | Max                                  |
| ------------------- | ---------------------------------------------------------------------------------------------------------------------- | ------------- | ----- | ------------------------------------ |
| Enabled             | Enable/Disable RTV functionality                                                                                       | true          | false | true                                 |
| EnabledInWarmup     | Enable/Disable RTV during warmup                                                                                       | false         | false | true                                 |
| NominationEnabled   | Enable/Disable nomination                                                                                              | true          | false | true                                 |
| MinPlayers          | Minimum amount of players to enable RTV/Nominate                                                                       | 0             | 0     |                                      |
| MinRounds           | Minimum rounds to enable RTV/Nominate                                                                                  | 0             | 0     |                                      |
| ChangeMapImmediately | Whether to change the map immediatly when vote ends or not                                                             | true          | false | true                                 |
| HudMenu             | Whether to use HudMenu or just the chat one, when 0 the hud only shows which map is winning instead of actual menu. 0 = Chat only, 1 = CenterHTML HUD, 2 = ScreenMenuHUD| 1          | 0 | 2                                 |
| HideHudAfterVote    | Whether to hide vote status hud after vote or not, only matters when HudMenu is true                                   | false         | false | true                                 |
| MapsToShow          | Amount of maps to show in vote,                                                                                        | 6             | 1     | 6 with HudMenu, unlimited without it |
| VoteDuration        | Seconds the RTV should last                                                                                            | 30            | 1     |                                      |
| VotePercentage      | Percentage of players that should type RTV in order to start a vote                                                    | 60            | 0     | 100                                  |
| DontChangeRtv       | Enable/Disable option not to change the current map                                                                    | true          | false | true                                 |
| IgnoreSpec          | Ignore spectators from vote count                                                                                      | true          | false | true                                 |
| VoteCooldownTime    | Cooldown timer to start the next RTV                                                                                   | 300           | 0     | -                                 |
| ExtendTimeStep        | How long (in minutes) should the mp_timelimit to be extended                                                             | 15         | 0       | -                                |
| ExtendRoundStep         | How many rounds should the mp_maxrounds to be extended                                                                 | 5             | 0     |                                      |
| ExtendLimit             | How many times the current map can be extended (-1 for unlimited extensions)                                          | 3             | -1     |                                      |

## End of map vote
Based on `mp_timelimit` and `mp_maxrounds` cvar before the map ends a RTV like vote will start to define the next map, it can be configured to change immediatly or only when the map actually ends

| Config                  | Description                                                                                                            | Default Value | Min   | Max                                  |
| ----------------------- | ---------------------------------------------------------------------------------------------------------------------- | ------------- | ----- | ------------------------------------ |
| Enabled                 | Enable/Disable end of map vote functionality                                                                           | true          | false | true                                 |
| ChangeMapImmediately     | Whether to change the map immediatly when vote ends or not                                                             | true          | false | true                                 |
| HideHudAfterVote        | Whether to hide vote status hud after vote or not, only matters when HudMenu is true                                   | false         | false | true                                 |
| MapsToShow              | Amount of maps to show in vote,                                                                                        | 6             | 1     | 6 with HudMenu, unlimited without it |
| VoteDuration            | Seconds the RTV should last                                                                                            | 30            | 1     |                                      |
| HudMenu                 | Whether to use HudMenu or just the chat one, when 0 the hud only shows which map is winning instead of actual menu. 0 = Chat only, 1 = CenterHTML HUD, 2 = ScreenMenuHUD | 1          | 0 | 2                                 |
| TriggerSecondsBeforeEnd | Amount of seconds before end of the map that should trigger the vote, only used when mp_timelimit is greater than 0    | 120           | 1     |                                      |
| TriggerRoundsBeforeEnd   | Amount of rounds before end of map that should trigger the vote, only used when mp_maxrounds is set                    | 2             | 1     |                                      |
| DelayToChangeInTheEnd   | Delay in seconds that plugin will take to change the map after the win panel is shown to the players                   | 6             | 3     |                                      |
| AllowExtend             | Option to extend the current map (Also needs to configure ExtendLimit)                                                 | true          | false | true                                 |
| RoundBased              | Whether to extend `mp_timelimit` or extend current round `mp_roundtime`                                                | true          | false | true                                 |
| ExtendTimeStep          | How long (in minutes) should the mp_timelimit to be extended                                                           | 15            | 0     |                                      |
| ExtendRoundStep         | How many rounds should the mp_maxrounds to be extended                                                                 | 5             | 0     |                                      |
| ExtendLimit             | How many times the current map can be extended (-1 for unlimited extensions)                                           | 3             | -1     |                                      |

## Extend map vote
Players can extend the current map by using the `!ext` command. Extends the `mp_timelimit` and `mp_maxrounds` cvar

| Config                  | Description                                                                                                            | Default Value | Min   | Max                                  |
| ----------------------- | ---------------------------------------------------------------------------------------------------------------------- | ------------- | ----- | ------------------------------------ |
| Enabled                 | Enable/Disable extend map vote functionality                                                                           | true          | false | true                                 |
| EnabledInWarmup         | Enable/Disable EXT during warmup                                                                                       | true          | false | true                                 |
| MinRounds               | Minimum rounds to enable ext                                         | 0             |       |      |
| MinPlayers              | Minimum amount of players to enable ext                              |               |       |      |
| VotePercentage		  | Percentage of players that should vote in a map in order to extend it												   | 60            | 1     | 100								  |
| ChangeMapImmediately     | Placeholder field. Keep it as false to prevent breaking the plugin function                                            | false         | false | true                                 |
| ExtendTimeStep          | How long (in minutes) should the mp_timelimit to be extended                                                           | 15            | 0     |                                      |
| ExtendRoundStep         | How many rounds should the mp_maxrounds to be extended                                                                 | 5             | 0     |                                      |
| ExtendLimit             | How many times the current map can be extended (-1 for unlimited extensions)                                          | 3             | -1     |                                      |
| RoundBased              | Whether to extend `mp_timelimit` or extend current round `mp_roundtime`                                                | true          | false | true                                 |
| IgnoreSpec              | Ignore spectators from vote count																					   | true          | false | true								  |


## VIP Extend map vote
Players can extend the current map by using the `!ve` or `!voteextend` command. Requires `@css/vip` flag permission. Extends the `mp_timelimit` and `mp_maxrounds` cvar

| Config                  | Description                                                                                                            | Default Value | Min   | Max                                  |
| ----------------------- | ---------------------------------------------------------------------------------------------------------------------- | ------------- | ----- | ------------------------------------ |
| Enabled                 | Enable/Disable extend map vote functionality                                                                           | true          | false | true                                 |
| VoteDuration            | Seconds the VIP extend vote should last                                                                                | 30            | 1     |                                      |
| VotePercentage		  | Percentage of players that should vote in a map in order to extend it												   | 60            | 1     | 100								  |
| ExtendTimeStep          | How long (in minutes) should the mp_timelimit to be extended                                                           | 15            | 0     |                                      |
| ExtendRoundStep         | How many rounds should the mp_maxrounds to be extended                                                                 | 5             | 0     |                                      |
| ExtendLimit             | How many times the current map can be extended (-1 for unlimited extensions)                                           | 3             | -1     |                                      |
| RoundBased              | Whether to extend `mp_timelimit` or extend current round `mp_roundtime`                                                | true          | false | true                                 |
| HudMenu                 | Whether to use HudMenu or just the chat one. 0 = Chat only, 1 = CenterHTML HUD, 2 = ScreenMenuHUD                      | 1          | 0 | 2                                 |


## Votemap
Players can vote to change to an specific map by using the votemap <mapname> command

| Config              | Description                                                              | Default Value | Min   | Max  |
| ------------------- | ------------------------------------------------------------------------ | ------------- | ----- | ---- |
| Enabled             | Enable/disable votemap funtionality                                      | true          | false | tru  |
| VotePercentage      | Percentage of players that should vote in a map in order to change to it | 60            | 1     | 100  |
| ChangeMapImmediately | Whether to change the map immediatly when vote ends or not               | true          | false | true |
| EnabledInWarmup     | Enable/Disable votemap during warmup                                     | true          | false | true |
| MinRounds           | Minimum rounds to enable votemap                                         | 0             |       |      |
| MinPlayers          | Minimum amount of players to enable votemap                              |               |       |      |
| HudMenu             | Whether to use HudMenu or just the chat one. 0 = Chat only, 1 = CenterHTML HUD, 2 = ScreenMenuHUD    | 1          | 0 | 2 |
| IgnoreSpec          | Ignore spectators from vote count                                        | true          | false | true |


## Timeleft
Players can type `timeleft` to see how much time is left in the current map 

| Config    | Description                                                                      | Default Value | Min   | Max  |
| --------- | -------------------------------------------------------------------------------- | ------------- | ----- | ---- |
| ShowToAll | Whether to show command response to everyone or just the player that executed it | false         | false | true |

## Nextmap
Players can type `nextmap` to see which map is going to be played next

| Config    | Description                                                                      | Default Value | Min   | Max  |
| --------- | -------------------------------------------------------------------------------- | ------------- | ----- | ---- |
| ShowToAll | Whether to show command response to everyone or just the player that executed it | false         | false | true |


  
# Adding workshop maps
- If you are not hosting a collection in order to add workshop maps you need to know it's id and add as following in the maplist.txt file: `<mapname>:<workshop-id>`.
- If you are already hosting a collection and can change to workshop maps using the command `ds_workshop_changelevel <map-name>` you don't need the ID, just put the actual map name and it will work.
    - However, it's still recommended to add the ID	if you plan to modify the display map name.

```
de_thera:3121217565
de_dust2
```

# Server commands

- rtv [seconds] - Trigger a map vote externally that will change the map immediately with an optional seconds parameter for voting duration (useful for gamemodes like GunGame)

# Admin commands
- css_extend [minutes] - Extend the current map time limit mp_timelimit by minutes 

# Vip commands
Requires "@css/vip" permission
- css_ve or css_voteextend - Vote to initialise extending the current map

# Limitations
 - Plugins is still under development and a lot of functionality is still going to be added in the future.
 - I usually test the new versions in an empty server with bots so it is hard to tell if everything is actually working, feel free to post any issues here or in the discord thread so I can fix them https://discord.com/channels/1160907911501991946/1356499757362843678
