# PassengerHelper v2 change log

## New Features
- Passenger settings are now saved to the game save instead of an external json file.
    - Reduces reliance on RailLoader
    - Should address issue where engines with the same reporting mark and number would overwrite each other when switching between saves
    - makea the settings multiplayer friendly. They work in MP with live updates between players.
- Passenger Settings button is now visible as long as passenger cars are coupled to the engine, independent of AE Mode
- Continue button will only appear if engine is in AERoad or AEWaypoint and it is currently stopped at a station for a defined reason(coal, water, diesel, etc)
- Added Reset button to CarPanel UI. This button will reset the settings cache and effectively make it so the station procedure will re-run at the given station. Useful for mistakes and weird oddities that sometimes occur.
    - this will only be visible if the cache has been set, which is when a train is at a station.


