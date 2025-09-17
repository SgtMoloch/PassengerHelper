# Version 2 Testing Plan

## Settings
### Regression Testing
- all buttons should work as they did in v1.2
- all tooltips should display as they did in v1.2
- all interactions should work as they did in v1.2
- settings should persist after closing and reopening the settings window
- settings should persist after pressing Save Settings and reopening the settings window
- Changing direction manually while in AERoad should change Direction of Travel to Unknown
### New Testing
- settings should save when closing the window, but reloading the save without saving should not persist the changed settings
- settings should persist between save loads when saving before reloading
- settings should be seen in MP, setting changes by anyone should cause the updated value to show up for everyone else

## Car Inspector
### Regression Testing
- Settings button should open the settings window
- continue button should appear when train is paused at a station
- continue button when pressed should cause the train to continute
### New Testing
- Settings button should be visible regardless of Mode, so long as there are coaches coupled to the engine. No coaches, no button.
- Reset Button should be visible if and only if the train is at a station and has ran a station procedure

## Station Procedure
### Bugfix
- When running To Alarka with Jct as a transfer station, should correctly pick up all pickup passengers if to the west of jct, including those to the east of jct
    - previously it would not select the stations further east
### Manual Mode
#### New Testing
- Passeneger Helper should work in manual mode now.
    - Direction of travel should still be set automatically when certain conditions are met 
        - previous station is known
        - train is not at Cochran with Alarka as previous stop
        - train is not at Alarka with Cochran as previous stop
    - Unknown Direction of Travel in certain cases should cause a message to appear in the in game console
        - train is at Cochran with Alarka as previous stop
        - train is at Alarka with Cochran as previous stop
    - stations should be selected on coaches as appropiate
    - transfer stations should work as appropiate
    - train should not need to wait until it is centered on the station span to run the procedure
    - train should be unaffected by any 'Pause at/Pause for' settings
    - passenger helper should work in this mode for custom stations
    - reverser direction should be automatically changed
### AERoad Mode
#### Regression Testing
- stations should be auto selected as before
- auto direction changing should work as before
- pause at and pause for functionality should work as before
#### New Testing
- Test with custom stations
### AEWaypoint Mode 
#### New Testing
- 