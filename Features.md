# Passenger Helper Features
Note that in this document, stop and pause are used in conjunction and effectively mean the same thing.

All settings are set per individual locomotive.

Settings are saved under the following conditions:
- Save Settings Button pressed
- Passenger Settings Window closed
- Return to Home screen
- Normal Operation of Passenger Helper
    - Passenger Helper will save settings at each terminus station and at all other stations if the Direction of Travel was changed by Passenger Helper

All settings are preserved between game sessions.
- Note that game crashes are NOT covered. Only the conditions above will result in settings being saved.

## Passenger Settings
This menu can be accessed by pressing the button on the Road AI panel.
- Note that if there are no coaches coupled to the locomotive, this button will NOT be present. This is your <b><u>ONE AND ONLY</u></b> warning.
### Disable
There is a setting called Disable. Selecting this will disable Passenger Helper for that particular locomotive.

### Station Pause Settings

- #### Stop at Next Station
    Tells Passenger Helper to stop the train at the next station, regardless of where it is.
- #### Stop at Last Station
    Tells Passenger Helper to stop the train at the next terminus station. (This setting will be renamed in a future version to better reflect the proper terminology. Tooltip accurately states the feature)
- #### Stop for Low Fuel
    Tells Passenger Helper to stop the train at any station if the coal/water/diesel level falls below the given percent. The percent is configurable, from values of 0 to 99.
- #### Wait for Full Load of Passengers At Last Station
    Tells Passenger Helper to keep a train paused at a terminus station if the train is not full of passengers. (This setting will be renamed in a future version to better reflect the proper terminology. Tooltip accurately states the feature)

### Station Selection

- #### Selected Station
    Tells Passenger Helper which stations you want the train to stop at. Also serves as the list of stations to select on the coupled passenger cars.
- #### Terminus Station
    It is required to have two terminus stations selected, otherwise the train will remain paused at the next station as a result of this. This setting will tell Passenger Helper which stations are the terminus stations, with terminus meaning "End of Route". It is at these stations that Passenger Helper will reverse the train (if Point to Point Mode is selected) as well as reselect all stations going in the opposite direction.
- #### Station Action
    This drop down allows you to fine grain which station you would like Passenger Helper to stop a train at. Normal means no stopping, Pause means to Stop/Pause.

### Passenger Mode

- #### Point to Point Mode
    This setting tells Passenger Helper to operate in Point to Point mode, where when it reaches a terminus station, will reverse the direction of the train. Also applies to Alarka Deport regardless if it is a terminus station or not.
- #### Loop Mode
    This settings tells Passenger Helper to operate in Loop mode, where when it reaches a terminus station, will continue moving in the current direction, and assumes that you have a loop at that station AND the switches are aligned for the train to use them.

### Direction of Travel
This setting tells Passenger Helper which direction the train is traveling in. 
- #### East
    This means that the train is traveling East (for example towards Whittier)
- #### Unknown
    This means that the train is traveling in an Unknown direction. If this is the Direction of travel, the train will pause at the next station under certain circumstances to be discussed later.
- #### West
    This means that the train is traveling West (for example towards Bryson/Andrews)

## Automatic Station Selection

This was touched on above. When at any given station, Passenger Helper will ensure that all stations in the route between the two terminus stations are selected on the coupled passenger cars. This also extends to Alarka, where it will reselect Cochran, assuming the route you are taking goes past Alarka, on to Almond, or vice versa.

Additionally as mentioned above, at each terminus station, Passenger Helper will select all stops between the current terminus station and the opposite terminus station.

## Direction Intelligence

Passenger Helper has assisted Direction Intelligence. This means that there are some cases where Passenger Helper cannot determine the Direction of Travel and must receive external input to figure it out. These situations (not an exhaustive list) include:
- Reaching a terminus station for the first time, AND the direction of travel is Unknown.
- Reaching any station for the first time, the direction of travel is Unknown, AND there have been no previous station stops during the use of Passenger Helper.
    - Note, If there has been at least one previous station stop during the use of Passenger Helper, Passenger Helper will be able to determine the Direction of Travel.
- Reaching Alarka Depot
    - If Direction of travel is Unknown
    - If you approach from the east side of Alarka Depot (the Wye side), the train will reverse if it is in Point to Point Mode (If you approach from this direction, it is best to set Direction of Travel to Unknown until after the train stops at the station)

If Passenger Helper has assumed control over the Direction of Travel, this setting will be disabled in Passenger Settings and you will be unable to change it.

If you change anything with the Controls of the train:
- AI Mode
- AI Direction
- AI Speed

the Direction of Travel, if previously controlled by Passenger Helper, will change to Unknown. 

Passenger Helper will never assume control over an Unknown Direction.

If Passenger Helper does not have control over the Direction of Travel, then modifying the controls as listed above will NOT change the direction of travel to unknown. Again, it will ONLY change to unknown if Passenger Helper had control over the direction of travel AND you change the controls.

## Passenger Stops
Under the following conditions, Stations will not start Loading/Unloading passengers until the train has actually "Arrived" (This is indicated by the console message saying as much) at the station:
- Passenger Helper is not disabled in settings
- The Train is in Road mode

Under the following conditions, Stations WILL start Loading/Unloading passengers if even ONE passenger car is in the Passenger Station span:
- Train is in Manual Mode
- Train is in Yard Mode
- Passenger Helper is disabled for that train

If the train leaves a station and then you use manual intervention to return to the same station, Passenger Helper will most likely <b>NOT</b> work properly. This is your <b><u>ONE AND ONLY</u></b> warning.

## Continue Button
IF the train has stopped for any reason, a Continue button will be presented on the Road AI panel. Pressing this button will tell Passenger Helper to continue. 
- Note: If the direction of travel is Unknown, there is <b><u>NO</u></b> safeguard against pressing this button. Pressing it when this is the case will cause the train to just continue in the current direction according to its reverser. This is your <b><u>ONE AND ONLY</u></b> warning.