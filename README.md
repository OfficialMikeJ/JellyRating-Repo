# Jellyfin Parental Rating Manager Plugin
## Target Version
Jellyfin Server 12.1.0

## Purpose

Develop a Jellyfin server plugin that allows the Jellyfin server owner/administrator to manually manage parental content ratings for media and control which Jellyfin users are allowed to see or access content based on those ratings.

The plugin must support Canadian and United States parental/content ratings.

The system must work independently of whether Jellyfin or the media metadata provider already supplied a correct rating.

The administrator must always have the ability to manually override a media item's rating through this plugin.

---

# 1. Supported Libraries

The plugin must allow the administrator to select and manage individual Jellyfin media libraries.

The initial libraries that must be supported are:

- Movies
- TVShows
- Documentaries

Do not hard-code functionality exclusively to these three names.

The plugin should discover Jellyfin media libraries dynamically so additional libraries can be supported later.

Each library must be selectable independently from the plugin administration page.

---

# 2. Media Browser

After selecting a library, display the media contained inside that library.

The interface must make it easy to manage ratings without displaying every individual episode.

## Movies

Display each movie as one selectable item.

Example:

[ ] John Wick  
[ ] Deadpool  
[ ] Oppenheimer

The administrator can check one or more movies and assign a rating.

---

## Documentaries

Display each documentary as one selectable item.

If a documentary is represented as a single Jellyfin media item, treat it similarly to a movie.

Example:

[ ] Planet Earth  
[ ] The Last Dance  
[ ] World War II Documentary

---

## TV Series

TV content must use a hierarchy.

Example:

Tulsa King  
- [ ] Entire Series
- [ ] Season 1
- [ ] Season 2
- [ ] Season 3

Do NOT display individual episodes for parental-rating assignment.

Ratings are assigned at:

- Series level
- Season level

Never require the administrator to rate individual episodes.

---

# 3. Series Rating Inheritance

If the administrator assigns a rating to an entire TV series, every season and episode under that series must inherit that rating unless a season has an explicit override.

Example:

Tulsa King = 18A

Season 1 automatically = 18A  
Season 2 automatically = 18A  
Season 3 automatically = 18A

If the administrator later changes:

Season 1 = 14A

Then:

Season 1 and all episodes inside Season 1 inherit 14A.

Season 2 and Season 3 continue inheriting the parent series rating of 18A.

Season-level overrides take priority over the series-level rating.

---

# 4. Rating Assignment Interface

Each selectable media item must contain a checkbox.

The administrator must be able to:

1. Select one item.
2. Select multiple items.
3. Select an entire series.
4. Select individual seasons.
5. Select multiple movies.
6. Select multiple documentaries.
7. Apply one rating to all selected items.

Provide a rating dropdown.

Provide a button:

Save Ratings

The rating must NOT be changed until the administrator clicks Save Ratings.

---

# 5. Rating Systems

Support ratings commonly used in both Canada and the United States.

The plugin must normalize metadata variations.

For example:

PG

PG&#x20;

PG-13

Rated R

R

18-A

18A

18-A&#x20;

These variations must not be treated as completely different ratings when they represent the same classification.

Trailing spaces, HTML encoding, capitalization differences, and common formatting variations must be cleaned before the rating is evaluated.

---

# 6. United States Ratings

At minimum support:

## Movies

- G
- PG
- PG-13
- R
- NC-17
- Unrated
- Not Rated

## Television

- TV-Y
- TV-Y7
- TV-G
- TV-PG
- TV-14
- TV-MA
- Unrated
- Not Rated

The plugin architecture should allow additional US classifications to be added later.

---

# 7. Canadian Ratings

Canadian classifications can differ depending on province and media type.

The plugin should at minimum recognize common classifications such as:

- G
- PG
- 14A
- 18A
- R
- A

Also support common formatting variants such as:

- 14-A
- 18-A
- 14A
- 18A

The rating architecture must be configurable rather than hard-coded into complicated application logic.

The developer should be able to add additional Canadian ratings later without redesigning the entire plugin.

---

# 8. Normalized Internal Rating Model

Internally the plugin should use a normalized rating representation.

Example concept:

G  
PG  
PG-13  
14A  
R  
18A  
NC-17  
TV-Y  
TV-Y7  
TV-G  
TV-PG  
TV-14  
TV-MA  
Unrated

Raw metadata values should be translated into the normalized value before access rules are evaluated.

Example:

"18-A " → 18A

"Rated R" → R

"PG " → PG

"PG&#x20;" → PG

Normalization must happen before comparisons are made.

---

# 9. User Management

The plugin must display all Jellyfin user accounts that the current administrator has permission to manage.

Each user should have a checkbox.

Example:

[ ] Parent  
[ ] Child 1  
[ ] Child 2  
[ ] Guest

Multiple users may be selected at once.

The administrator must also be able to configure one user independently.

---

# 10. Per-User Maximum Allowed Rating

Each user may be assigned a maximum permitted content level.

Example concept:

Child Account:

Maximum Allowed Rating: PG-13

Content rated at or below the permitted level may be visible.

Content above the permitted level must be hidden and inaccessible.

The exact mapping between Canadian and United States ratings must be handled by a clearly defined rating policy instead of comparing rating names alphabetically.

---

# 11. User Content Restrictions

When a user is restricted from content, the plugin must enforce the restriction server-side.

Do NOT rely only on hiding the item in the web interface.

Restricted users must not be able to access blocked media through:

- Jellyfin Web
- Jellyfin mobile clients
- Jellyfin TV applications
- Jellyfin desktop clients
- Search
- Library browsing
- Recommendations
- Recently Added
- Continue Watching
- Direct item URLs
- Playback API requests
- Direct streaming requests where Jellyfin authorization normally applies

The access decision must ultimately be enforced by the Jellyfin server.

UI hiding alone is not acceptable security.

---

# 12. Restricted Content Visibility

By default, media above a user's permitted rating should not appear to that user.

The media should behave as though the user does not have access to it.

It must not expose:

- Title
- Poster
- Description
- Cast information
- Episode names
- Season information
- Playback links
- Search results

This prevents younger users from seeing inappropriate titles or artwork even when playback itself would otherwise be blocked.

---

# 13. Example

Administrator configures:

Child User Maximum Rating = PG-13

Library contains:

Movie A = PG  
Movie B = PG-13  
Movie C = R  
Movie D = 18A

Expected result:

Movie A = Accessible  
Movie B = Accessible  
Movie C = Restricted  
Movie D = Restricted

The restricted movies should not normally appear to that user.

---

# 14. TV Series Example

Tulsa King:

Series Rating = 18A

Season 1 = inherits 18A  
Season 2 = inherits 18A  
Season 3 = inherits 18A

A user restricted below 18A must not see:

Tulsa King

or any of its seasons or episodes.

If the administrator changes:

Season 1 = PG-13

while the parent series remains:

Tulsa King = 18A

The plugin must decide visibility carefully.

The user may only access Season 1 if the application's Jellyfin integration can safely expose that season without unintentionally exposing metadata or access to the restricted seasons.

Access enforcement must always follow the most specific applicable rating:

Episode explicit rating, if ever supported in the future  
→ Season override  
→ Series rating  
→ Existing Jellyfin media rating  
→ Unrated policy

For the current plugin, administrators do not manually manage episode ratings.

---

# 15. Rating Source Priority

When determining the effective rating, use the following priority:

1. Plugin season override
2. Plugin series override
3. Plugin movie/documentary override
4. Existing Jellyfin/content metadata rating
5. Unrated-content policy

Manual plugin assignments must take priority over automatically imported metadata.

---

# 16. Existing Jellyfin Ratings

Do not destroy the media's original metadata unnecessarily.

If Jellyfin already knows that a movie is rated R, the plugin may use that information.

If the administrator manually changes the plugin rating to PG-13, the plugin override becomes the effective parental-control rating.

If the override is later removed, the original Jellyfin metadata rating can become active again.

---

# 17. Unrated Content

Provide an administrator setting controlling how unrated media is treated.

Options should include:

Allow Unrated Content

Block Unrated Content

Treat Unrated Content As A Specific Rating

For child accounts, administrators may choose to block unrated media completely.

Unrated content must never silently bypass configured parental restrictions unless the administrator explicitly allows it.

---

# 18. Bulk Rating

The administrator must be able to select several media items simultaneously.

Example:

[✓] Movie 1  
[✓] Movie 2  
[✓] Movie 3  
[✓] Documentary 1

Rating:

PG-13

Click:

Save Ratings

All selected media receives the chosen plugin rating.

---

# 19. Select All

Provide convenient bulk-selection controls.

Examples:

Select All

Deselect All

Select All Movies

Select All Series

Select All Seasons for This Series

These controls must only affect the currently relevant library/view unless clearly stated otherwise.

---

# 20. Search and Filtering

Large Jellyfin libraries require search.

Provide a search field capable of filtering media by:

- Title
- Series
- Movie
- Documentary

Useful filters should include:

- All
- Rated
- Unrated
- Manually Rated
- Rating value

Example:

Show only: 18A

This allows the administrator to review restricted content quickly.

---

# 21. Current Rating Display

Every displayed media item should show its current effective rating.

Example:

Tulsa King — 18A  
Season 1 — 18A (Inherited)  
Season 2 — PG-13 (Override)

The interface should distinguish:

- Metadata Rating
- Plugin Override
- Inherited Rating

The administrator should always be able to understand why an item currently has a particular rating.

---

# 22. Remove Override

Provide a way to remove a manually assigned plugin rating.

Example button/action:

Remove Override

Removing an override should restore the next available rating source.

Example:

Plugin Override = R  
Jellyfin Metadata = PG-13

Remove Override

Effective Rating becomes:

PG-13

Do not permanently erase the existing Jellyfin metadata just because an override was removed.

---

# 23. Admin-Only Access

Only authorized Jellyfin administrators should be allowed to configure the plugin.

Normal users must never have access to:

- Plugin settings
- Rating assignments
- User restriction settings
- Rating override controls
- Internal plugin database
- Access policies

All administrative endpoints must verify Jellyfin administrative authorization server-side.

Do not rely only on hiding administrator buttons.

---

# 24. Server-Side Authorization

Every access-control decision must use the authenticated Jellyfin user identity.

Never trust:

- User IDs supplied only by the browser
- Client-side JavaScript permissions
- Hidden UI controls
- Client-provided rating values

Authorization must be validated server-side.

---

# 25. Plugin Database

Store plugin-managed configuration separately from the actual video files.

Do not rename, modify, move, or rewrite media files.

The plugin data should include at minimum:

Media item identifier  
Library identifier  
Assigned rating  
Assignment type  
User restriction configuration  
Last modified date  
Plugin configuration version

Where possible, use stable Jellyfin item identifiers instead of relying solely on filesystem paths.

---

# 26. Media Changes

The plugin must handle Jellyfin library rescans safely.

If files are rescanned but Jellyfin retains the same logical media item, parental settings should remain.

If an item is deleted, stale plugin records should eventually be cleaned safely.

Never accidentally apply an old rating to an unrelated new media item because a filename or filesystem location was reused.

---

# 27. No File Modification

The plugin must NEVER:

- Alter video contents
- Re-encode media
- Rename media files automatically
- Delete media
- Move media
- Change folder permissions
- Modify operating-system file ownership
- Embed ratings directly into media files unless a future feature explicitly adds this

The plugin manages Jellyfin authorization and plugin metadata only.

---

# 28. Fail-Safe Behaviour

Parental restrictions must fail safely.

If the plugin cannot determine whether restricted content should be allowed for a restricted user, prefer denying access rather than accidentally allowing inappropriate content.

Administrator accounts should not accidentally become locked out because of malformed rating metadata.

Errors should be logged for investigation.

---

# 29. Logging

Log important administrative actions.

Examples:

Rating changed

Rating override removed

User parental policy changed

Bulk rating operation performed

Plugin configuration changed

Access denied because of parental restriction

Do not flood Jellyfin logs with unnecessary entries for every normal library query.

Debug logging may provide additional details when explicitly enabled.

Never log authentication tokens or sensitive credentials.

---

# 30. Plugin Administration Pages

Suggested page structure:

## Dashboard

Overview of:

- Number of rated items
- Number of unrated items
- Number of restricted users
- Plugin status

## Media Ratings

Choose Library:

Movies  
TVShows  
Documentaries

Then manage media ratings.

## User Restrictions

Display Jellyfin users and their maximum permitted ratings.

## Rating Configuration

Configure:

- USA rating definitions
- Canadian rating definitions
- Rating normalization mappings
- Unrated policy

## Plugin Settings

General plugin options and diagnostics.

---

# 31. Save Behaviour

Changing a dropdown must not immediately write changes.

The administrator should:

Select media  
Choose rating  
Click Save Ratings

After saving:

Display a clear success message.

Example:

Ratings saved successfully.

If saving fails:

Display a useful error.

Example:

Unable to save rating changes. Check the Jellyfin server log for details.

Do not silently fail.

---

# 32. Confirmation for Large Changes

For large bulk operations, request confirmation.

Example:

You are about to assign 18A to 147 media items.

Continue?

This reduces accidental mass-rating mistakes.

Do not require unnecessary confirmation for changing one ordinary media item.

---

# 33. Performance

Do not load thousands of Jellyfin items into the browser at once.

Use one or more of:

- Pagination
- Lazy loading
- Server-side filtering
- Incremental loading

The plugin must remain usable with large libraries.

Avoid repeatedly scanning the physical filesystem when Jellyfin's library database/API already contains the required media hierarchy.

---

# 34. Do Not Build a Separate Media Scanner Unless Required

Jellyfin should remain the primary source for:

- Libraries
- Movies
- Series
- Seasons
- Episodes
- Users
- Item identifiers
- Existing parental-rating metadata

Do not independently crawl `/mnt/storage` or other media directories unless there is a technical Jellyfin limitation that makes this unavoidable.

Use Jellyfin's library representation wherever possible.

---

# 35. Folder Versus Jellyfin Item Concept

The administration UI may visually represent media similar to its folder organization.

However, authorization must be based primarily on Jellyfin media entities rather than blindly trusting filesystem folders.

Example:

Tulsa King  
→ Series

Season 1  
→ Jellyfin Season

Episodes  
→ Jellyfin Episodes

This keeps the plugin compatible with Jellyfin's library model.

---

# 36. Content Type Rules

Only show valid rating targets.

Movie:

Selectable

Documentary:

Selectable

Series:

Selectable

Season:

Selectable

Episode:

Not selectable in the current version

Random non-media files:

Not selectable

Images/subtitles/NFO files:

Not selectable

---

# 37. Rating Policy Architecture

Keep the rating comparison system separated from the UI.

Do not scatter logic such as:

if rating == "PG-13"

throughout the codebase.

Create a centralized rating policy/service responsible for:

- Normalization
- Rating lookup
- Rating comparison
- Country/system mapping
- Unrated behavior

This will make future maintenance much easier.

---

# 38. Canadian and American Cross-System Handling

Do not assume that Canadian and US classifications have identical names or direct one-to-one equivalents.

Create an explicit policy table/configuration describing their relative access levels.

The plugin should evaluate a normalized parental-control level while still displaying the original recognizable rating to the administrator.

Do not guess equivalency dynamically from rating text.

---

# 39. Security Rule

The most important technical rule:

HIDING CONTENT IS NOT THE SAME AS BLOCKING CONTENT.

The plugin must prevent unauthorized playback/access at the server authorization layer wherever Jellyfin's plugin architecture provides supported integration points.

A restricted user must not be able to bypass the plugin merely by discovering an item ID or playback URL.

---

# 40. Compatibility Rule

Target Jellyfin 12.1.0.

Do not intentionally support deprecated Jellyfin APIs merely because older plugin examples use them.

Before implementing an integration point:

Confirm it exists in the Jellyfin 12.1.0 server/plugin architecture being compiled against.

Do not invent APIs, classes, hooks, interfaces, or events.

If Jellyfin 12.1.0 does not provide a safe supported hook required for a feature, document the limitation rather than implementing a fragile fake workaround.

---

# 41. Plugin Isolation

The plugin must not modify Jellyfin core source code.

The server should remain upgradeable.

All functionality should live inside the plugin unless Jellyfin exposes no plugin-compatible mechanism for a required feature.

Any discovered Jellyfin limitation must be documented before proposing modification of Jellyfin itself.

---

# 42. Code Quality

Use clean separation between:

- Jellyfin integration
- Media discovery
- Rating normalization
- Rating policy
- User restrictions
- Persistence
- API endpoints
- Admin UI
- Logging

Avoid giant classes containing unrelated functionality.

Use dependency injection where appropriate within Jellyfin's supported plugin architecture.

---

# 43. Error Handling

Do not silently catch exceptions.

Handle expected errors cleanly and log useful diagnostic information.

The plugin must tolerate:

- Missing ratings
- Deleted media
- Deleted users
- Renamed libraries
- Invalid stored rating values
- Jellyfin metadata changes
- Plugin upgrades
- Empty libraries

One broken media item must not break the entire parental-control interface.

---

# 44. Data Validation

All values received from the administration UI must be validated server-side.

Validate:

- Jellyfin item ID
- User ID
- Library ID
- Rating identifier
- Content type
- Requested operation

Never accept an arbitrary rating string and save it without validation.

---

# 45. Initial Scope

Version 1 should focus on:

- Movies
- Documentaries
- TV series
- TV seasons
- USA ratings
- Canadian ratings
- Manual rating overrides
- Rating normalization
- User maximum-rating restrictions
- Hidden restricted media
- Server-side access enforcement
- Bulk rating
- Search/filtering
- Unrated-content policy
- Admin-only management

Do not expand Version 1 into unrelated parental-control features.

---

# 46. Features Not Required for Version 1

Do NOT add unless requested later:

- Per-episode manual rating
- Viewing schedules
- Daily screen-time limits
- Bedtime restrictions
- PIN unlock
- Purchase controls
- Remote parental approval
- Email notifications
- Mobile push notifications
- Automatic AI content classification
- Facial recognition
- Automatic age detection
- Automatic editing/censoring of movies
- Skipping violent or sexual scenes
- Media transcoding
- Separate child Jellyfin client

Keep Version 1 focused.

---

# 47. Future Expansion Compatibility

The architecture should make it possible to add later:

- More countries
- More rating systems
- Custom household rating levels
- PIN overrides
- Temporary parental approval
- Age-based user profiles
- Scheduling restrictions
- Additional Jellyfin media types

Do not implement these now.

Simply avoid designing Version 1 in a way that makes them impossible later.

---

# 48. Required User Workflow

The final administrator workflow should be simple:

1. Open Jellyfin Dashboard.
2. Open Parental Rating Manager.
3. Select Movies, TVShows, Documentaries, or another supported library.
4. Browse or search for media.
5. Check one or more movies, documentaries, series, or seasons.
6. Select a Canadian or US rating.
7. Click Save Ratings.
8. Open User Restrictions.
9. Select one or more Jellyfin users.
10. Set their maximum permitted content level.
11. Save the user policy.
12. Jellyfin automatically hides and blocks media above that user's permitted level.

No manual editing of configuration files should be required for normal use.

---

# 49. Example Complete Configuration

Libraries:

Movies  
TVShows  
Documentaries

Ratings:

Deadpool = R  
Oppenheimer = R  
Finding Nemo = G  
Tulsa King = 18A  
Tulsa King Season 1 = 18A  
Tulsa King Season 2 = 18A  
Tulsa King Season 3 = 18A  
Planet Earth = PG

Users:

Owner  
Maximum Rating: Unrestricted

Teenager  
Maximum Rating: PG-13 / configured Canadian equivalent level

Young Child  
Maximum Rating: PG

Expected behavior:

Owner sees everything.

Teenager does not see or access Deadpool, Oppenheimer, Tulsa King, or other content above their configured limit.

Young Child sees only content permitted under the PG policy.

The restriction applies regardless of which Jellyfin client the user uses.

---

# 50. Primary Development Rule

Do not consider the feature complete simply because restricted titles disappear from the Jellyfin web interface.

The plugin is complete only when rating assignment, rating inheritance, user policy evaluation, media filtering, and server-side access enforcement all operate together.

Security and authorization must be implemented first as server behavior.

The administration interface is the management layer on top of that behavior.

---

# 51. Testing Requirements

Create automated tests where practical for the rating-policy and normalization system.

At minimum test:

"PG" → PG

"PG " → PG

"PG&#x20;" after decoded input → PG

"Rated R" → R

"R" → R

"18-A" → 18A

"18A" → 18A

"14-A" → 14A

Unknown rating → Unrated/Unknown policy

Also test:

Movie override

Series inheritance

Season override

User below rating limit

User equal to rating limit

User above rating requirement

Unrestricted administrator

Unrated-content blocking

Deleted media reference

Deleted user reference

Bulk assignment

Override removal

---

# 52. Development Safety

Before writing access-control code that depends on Jellyfin internals:

Inspect the actual Jellyfin 12.1.0 assemblies/source/API available to the project.

Do not assume an older Jellyfin plugin tutorial remains valid.

Compile frequently.

Do not create placeholder integrations that appear functional while providing no real authorization protection.

If an intended hook does not exist, report exactly:

- What capability is required
- What Jellyfin provides
- What Jellyfin does not provide
- The safest supported alternative

Do not silently weaken the parental restriction requirement.

---

# Definition of Done

Version 1 is complete when an administrator can:

- Open the plugin inside Jellyfin administration.
- See Jellyfin libraries.
- Select Movies, TVShows, Documentaries, or another supported media library.
- See movies and documentaries individually.
- See TV shows by series and season without requiring episode-level management.
- Select items using checkboxes.
- Assign USA or Canadian ratings.
- Save those ratings.
- Bulk assign ratings.
- Override existing metadata ratings.
- Remove overrides.
- Search/filter the library.
- See inherited versus manually assigned ratings.
- View all manageable Jellyfin users.
- Configure parental restrictions per user or multiple users.
- Define treatment of unrated content.
- Prevent restricted media from appearing to restricted users.
- Prevent restricted media from being played or accessed through supported Jellyfin clients/API paths.
- Preserve settings across Jellyfin restarts.
- Survive normal library rescans.
- Log configuration errors appropriately.
- Operate without modifying the actual media files.
- Operate without modifying Jellyfin core source code.