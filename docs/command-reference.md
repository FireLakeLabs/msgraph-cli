# msgraph-cli Command Reference

**Version:** msgraph-cli 0.5.0

---

## Global Options

```
Options:
  --json          Output JSON to stdout
  --plain         Output tab-separated values to stdout
  --verbose       Verbose logging to stderr
  --beta          Use Microsoft Graph beta endpoint
  --read-only     Block write operations
  --dry-run       Show what would be done without executing
  -?, -h, --help  Show help and usage information
  --version       Show version information

```

## Commands

```
Commands:
  auth         Authentication and credential management
  mail         Outlook mail operations
  calendar     Calendar operations
  drive        OneDrive file operations
  todo         Microsoft To Do task operations
  excel        Excel workbook operations
  docs         Document operations (Word, PowerPoint)
  config       View and modify configuration
  completions  Generate shell completion scripts
  version      Show version information

```

---

## msgraph auth

```
Description:
  Authentication and credential management

Usage:
  msgraph auth [command] [options]

Options:
  -?, -h, --help  Show help and usage information

Commands:
  setup   Configure 1Password vault and store app registration
  login   Authenticate with Microsoft Entra ID
  status  Show current authentication state
  logout  Clear cached tokens from 1Password
  scopes  Display the scope registry for all services

```

### msgraph auth setup

```
Description:
  Configure 1Password vault and store app registration

Usage:
  msgraph auth setup [options]

Options:
  --client-id <client-id>  Entra ID Application (Client) ID
  --tenant-id <tenant-id>  Entra ID Directory (Tenant) ID
  -?, -h, --help           Show help and usage information

```

### msgraph auth login

```
Description:
  Authenticate with Microsoft Entra ID

Usage:
  msgraph auth login [options]

Options:
  --services <services>  Comma-separated list of services (mail,calendar,drive,todo,excel,docs)
  --readonly             Request only read scopes
  --device-code          Use device code flow (for headless/SSH)
  -?, -h, --help         Show help and usage information

```

### msgraph auth status

```
Description:
  Show current authentication state

Usage:
  msgraph auth status [options]

Options:
  -?, -h, --help  Show help and usage information

```

### msgraph auth logout

```
Description:
  Clear cached tokens from 1Password

Usage:
  msgraph auth logout [options]

Options:
  -?, -h, --help  Show help and usage information

```

### msgraph auth scopes

```
Description:
  Display the scope registry for all services

Usage:
  msgraph auth scopes [options]

Options:
  -?, -h, --help  Show help and usage information

```

---

## msgraph mail

```
Description:
  Outlook mail operations

Usage:
  msgraph mail [command] [options]

Options:
  -?, -h, --help  Show help and usage information

Commands:
  list                     List messages in a mail folder
  search <query>           Search messages across all folders
  get <messageId>          Get a specific message
  folders                  Mail folder operations
  send                     Send an email message
  reply <messageId>        Reply to a message
  forward <messageId>      Forward a message
  move <messageId>         Move a message to another folder
  mark-read <messageId>    Mark a message as read
  mark-unread <messageId>  Mark a message as unread
  attachments <messageId>  List or download message attachments

```

### msgraph mail list

```
Description:
  List messages in a mail folder

Usage:
  msgraph mail list [options]

Options:
  -f, --folder <f>  Mail folder name or ID (default: inbox)
  -n, --max <n>     Maximum number of messages to return
  -?, -h, --help    Show help and usage information

```

### msgraph mail search

```
Description:
  Search messages across all folders

Usage:
  msgraph mail search <query> [options]

Arguments:
  <query>  Search query (KQL syntax)

Options:
  -n, --max <n>   Maximum number of messages to return
  -?, -h, --help  Show help and usage information

```

### msgraph mail get

```
Description:
  Get a specific message

Usage:
  msgraph mail get <messageId> [options]

Arguments:
  <messageId>  Message ID

Options:
  --format <format>  Output detail level: summary or full [default: summary]
  -?, -h, --help     Show help and usage information

```

### msgraph mail folders

```
Description:
  Mail folder operations

Usage:
  msgraph mail folders [command] [options]

Options:
  -?, -h, --help  Show help and usage information

Commands:
  list  List mail folders

```

### msgraph mail send

```
Description:
  Send an email message

Usage:
  msgraph mail send [options]

Options:
  --to <to> (REQUIRED)            Recipient email address(es), comma-separated
  --subject <subject> (REQUIRED)  Email subject
  --body <body> (REQUIRED)        Email body (plain text)
  --body-html <body-html>         Email body (HTML, overrides --body)
  --cc <cc>                       CC recipient(s), comma-separated
  --bcc <bcc>                     BCC recipient(s), comma-separated
  --attach <attach>               File path(s) to attach
  -?, -h, --help                  Show help and usage information

```

### msgraph mail reply

```
Description:
  Reply to a message

Usage:
  msgraph mail reply <messageId> [options]

Arguments:
  <messageId>  Message ID to reply to

Options:
  --body <body> (REQUIRED)  Reply body text
  --all                     Reply to all recipients
  -?, -h, --help            Show help and usage information

```

### msgraph mail forward

```
Description:
  Forward a message

Usage:
  msgraph mail forward <messageId> [options]

Arguments:
  <messageId>  Message ID to forward

Options:
  --to <to> (REQUIRED)  Recipient email address(es), comma-separated
  --body <body>         Optional comment to include
  -?, -h, --help        Show help and usage information

```

### msgraph mail move

```
Description:
  Move a message to another folder

Usage:
  msgraph mail move <messageId> [options]

Arguments:
  <messageId>  Message ID to move

Options:
  --folder <folder> (REQUIRED)  Destination folder name or ID
  -?, -h, --help                Show help and usage information

```

### msgraph mail mark-read

```
Description:
  Mark a message as read

Usage:
  msgraph mail mark-read <messageId> [options]

Arguments:
  <messageId>  Message ID

Options:
  -?, -h, --help  Show help and usage information

```

### msgraph mail mark-unread

```
Description:
  Mark a message as unread

Usage:
  msgraph mail mark-unread <messageId> [options]

Arguments:
  <messageId>  Message ID

Options:
  -?, -h, --help  Show help and usage information

```

### msgraph mail attachments

```
Description:
  List or download message attachments

Usage:
  msgraph mail attachments <messageId> [options]

Arguments:
  <messageId>  Message ID

Options:
  --download           Download all attachments
  --out-dir <out-dir>  Output directory for downloads (default: current directory)
  -?, -h, --help       Show help and usage information

```

---

## msgraph calendar

```
Description:
  Calendar operations

Usage:
  msgraph calendar [command] [options]

Options:
  -?, -h, --help  Show help and usage information

Commands:
  list               List calendars
  events             List calendar events
  get <eventId>      Get event details
  search <query>     Search calendar events
  create             Create a calendar event
  update <eventId>   Update a calendar event
  delete <eventId>   Delete a calendar event
  respond <eventId>  Respond to a calendar event invitation
  freebusy           Check free/busy availability

```

### msgraph calendar list

```
Description:
  List calendars

Usage:
  msgraph calendar list [options]

Options:
  -?, -h, --help  Show help and usage information

```

### msgraph calendar events

```
Description:
  List calendar events

Usage:
  msgraph calendar events [options]

Options:
  --today                Show today's events
  --tomorrow             Show tomorrow's events
  --week                 Show this week's events
  --days <days>          Show events for next N days
  --from <from>          Start date (yyyy-MM-dd or ISO 8601)
  --to <to>              End date (yyyy-MM-dd or ISO 8601)
  --calendar <calendar>  Calendar ID
  -n, --max <n>          Maximum number of events
  -?, -h, --help         Show help and usage information

```

### msgraph calendar get

```
Description:
  Get event details

Usage:
  msgraph calendar get <eventId> [options]

Arguments:
  <eventId>  Event ID

Options:
  -?, -h, --help  Show help and usage information

```

### msgraph calendar search

```
Description:
  Search calendar events

Usage:
  msgraph calendar search <query> [options]

Arguments:
  <query>  Search query

Options:
  --from <from>   Start date filter
  --to <to>       End date filter
  -n, --max <n>   Maximum results
  -?, -h, --help  Show help and usage information

```

### msgraph calendar create

```
Description:
  Create a calendar event

Usage:
  msgraph calendar create [options]

Options:
  --subject <subject> (REQUIRED)  Event subject
  --start <start> (REQUIRED)      Start date/time (ISO 8601)
  --end <end> (REQUIRED)          End date/time (ISO 8601)
  --attendees <attendees>         Attendee emails, comma-separated
  --location <location>           Event location
  --body <body>                   Event body text
  --all-day                       Create as all-day event
  -?, -h, --help                  Show help and usage information

```

### msgraph calendar update

```
Description:
  Update a calendar event

Usage:
  msgraph calendar update <eventId> [options]

Arguments:
  <eventId>  Event ID to update

Options:
  --subject <subject>      New subject
  --start <start>          New start date/time
  --end <end>              New end date/time
  --location <location>    New location
  --attendees <attendees>  New attendees (comma-separated)
  -?, -h, --help           Show help and usage information

```

### msgraph calendar delete

```
Description:
  Delete a calendar event

Usage:
  msgraph calendar delete <eventId> [options]

Arguments:
  <eventId>  Event ID to delete

Options:
  -?, -h, --help  Show help and usage information

```

### msgraph calendar respond

```
Description:
  Respond to a calendar event invitation

Usage:
  msgraph calendar respond <eventId> [options]

Arguments:
  <eventId>  Event ID

Options:
  --status <status> (REQUIRED)  Response: accept, decline, or tentative
  --message <message>           Optional response message
  -?, -h, --help                Show help and usage information

```

### msgraph calendar freebusy

```
Description:
  Check free/busy availability

Usage:
  msgraph calendar freebusy [options]

Options:
  --from <from> (REQUIRED)  Start date/time (ISO 8601)
  --to <to> (REQUIRED)      End date/time (ISO 8601)
  --emails <emails>         Email addresses, comma-separated (default: self)
  -?, -h, --help            Show help and usage information

```

---

## msgraph drive

```
Description:
  OneDrive file operations

Usage:
  msgraph drive [command] [options]

Options:
  -?, -h, --help  Show help and usage information

Commands:
  ls                         List folder contents
  search <query>             Search for files
  get <itemId>               Get item details
  download <itemId>          Download a file
  upload <localPath>         Upload a file
  mkdir <name>               Create a folder
  move <itemId>              Move an item
  rename <itemId> <newName>  Rename an item
  delete <itemId>            Delete an item

```

### msgraph drive ls

```
Description:
  List folder contents

Usage:
  msgraph drive ls [options]

Options:
  --path <path>      Remote folder path
  --folder <folder>  Folder ID
  -n, --max <n>      Maximum items
  -?, -h, --help     Show help and usage information

```

### msgraph drive search

```
Description:
  Search for files

Usage:
  msgraph drive search <query> [options]

Arguments:
  <query>  Search query

Options:
  -n, --max <n>   Maximum results
  -?, -h, --help  Show help and usage information

```

### msgraph drive get

```
Description:
  Get item details

Usage:
  msgraph drive get <itemId> [options]

Arguments:
  <itemId>  Item ID

Options:
  -?, -h, --help  Show help and usage information

```

### msgraph drive download

```
Description:
  Download a file

Usage:
  msgraph drive download [<itemId>] [options]

Arguments:
  <itemId>  Item ID

Options:
  --path <path>           Remote file path
  --out <out> (REQUIRED)  Local output path
  -?, -h, --help          Show help and usage information

```

### msgraph drive upload

```
Description:
  Upload a file

Usage:
  msgraph drive upload <localPath> [options]

Arguments:
  <localPath>  Local file path

Options:
  --path <path> (REQUIRED)  Remote destination path
  -?, -h, --help            Show help and usage information

```

### msgraph drive mkdir

```
Description:
  Create a folder

Usage:
  msgraph drive mkdir <name> [options]

Arguments:
  <name>  Folder name

Options:
  --path <path>   Parent folder path
  -?, -h, --help  Show help and usage information

```

### msgraph drive move

```
Description:
  Move an item

Usage:
  msgraph drive move <itemId> [options]

Arguments:
  <itemId>  Item ID to move

Options:
  --destination <destination> (REQUIRED)  Destination folder path
  -?, -h, --help                          Show help and usage information

```

### msgraph drive rename

```
Description:
  Rename an item

Usage:
  msgraph drive rename <itemId> <newName> [options]

Arguments:
  <itemId>   Item ID to rename
  <newName>  New name

Options:
  -?, -h, --help  Show help and usage information

```

### msgraph drive delete

```
Description:
  Delete an item

Usage:
  msgraph drive delete <itemId> [options]

Arguments:
  <itemId>  Item ID to delete

Options:
  -?, -h, --help  Show help and usage information

```

---

## msgraph todo

```
Description:
  Microsoft To Do task operations

Usage:
  msgraph todo [command] [options]

Options:
  -?, -h, --help  Show help and usage information

Commands:
  lists                     Manage task lists
  list <listId>             List tasks in a task list
  get <listId> <taskId>     Get task details
  add <listId>              Add a new task
  update <listId> <taskId>  Update a task
  done <listId> <taskId>    Mark a task as completed
  undo <listId> <taskId>    Mark a completed task as not started
  delete <listId> <taskId>  Delete a task

```

### msgraph todo lists

```
Description:
  Manage task lists

Usage:
  msgraph todo lists [command] [options]

Options:
  -?, -h, --help  Show help and usage information

Commands:
  create <displayName>  Create a new task list

```

### msgraph todo list

```
Description:
  List tasks in a task list

Usage:
  msgraph todo list <listId> [options]

Arguments:
  <listId>  Task list ID

Options:
  --status <status>  Filter by status: incomplete or completed
  -n, --max <n>      Maximum number of tasks
  -?, -h, --help     Show help and usage information

```

### msgraph todo get

```
Description:
  Get task details

Usage:
  msgraph todo get <listId> <taskId> [options]

Arguments:
  <listId>  Task list ID
  <taskId>  Task ID

Options:
  -?, -h, --help  Show help and usage information

```

### msgraph todo add

```
Description:
  Add a new task

Usage:
  msgraph todo add <listId> [options]

Arguments:
  <listId>  Task list ID

Options:
  --title <title> (REQUIRED)  Task title
  --due <due>                 Due date (yyyy-MM-dd or ISO 8601)
  --body <body>               Task body text
  --importance <importance>   Importance: low, normal, or high
  -?, -h, --help              Show help and usage information

```

### msgraph todo update

```
Description:
  Update a task

Usage:
  msgraph todo update <listId> <taskId> [options]

Arguments:
  <listId>  Task list ID
  <taskId>  Task ID to update

Options:
  --title <title>            New title
  --due <due>                New due date
  --body <body>              New body text
  --importance <importance>  New importance: low, normal, or high
  -?, -h, --help             Show help and usage information

```

### msgraph todo done

```
Description:
  Mark a task as completed

Usage:
  msgraph todo done <listId> <taskId> [options]

Arguments:
  <listId>  Task list ID
  <taskId>  Task ID to mark complete

Options:
  -?, -h, --help  Show help and usage information

```

### msgraph todo undo

```
Description:
  Mark a completed task as not started

Usage:
  msgraph todo undo <listId> <taskId> [options]

Arguments:
  <listId>  Task list ID
  <taskId>  Task ID to reopen

Options:
  -?, -h, --help  Show help and usage information

```

### msgraph todo delete

```
Description:
  Delete a task

Usage:
  msgraph todo delete <listId> <taskId> [options]

Arguments:
  <listId>  Task list ID
  <taskId>  Task ID to delete

Options:
  -?, -h, --help  Show help and usage information

```

---

## msgraph excel

```
Description:
  Excel workbook operations

Usage:
  msgraph excel [command] [options]

Options:
  -?, -h, --help  Show help and usage information

Commands:
  sheets <itemId>  List worksheets in a workbook
  get <itemId>     Read a range from a worksheet
  update <itemId>  Update cell values in a range
  append <itemId>  Append rows to a table

```

### msgraph excel sheets

```
Description:
  List worksheets in a workbook

Usage:
  msgraph excel sheets <itemId> [options]

Arguments:
  <itemId>  Drive item ID of the Excel file

Options:
  -?, -h, --help  Show help and usage information

```

### msgraph excel get

```
Description:
  Read a range from a worksheet

Usage:
  msgraph excel get <itemId> [options]

Arguments:
  <itemId>  Drive item ID of the Excel file

Options:
  --sheet <sheet> (REQUIRED)  Worksheet name
  --range <range> (REQUIRED)  Range address (e.g. A1:D20)
  -?, -h, --help              Show help and usage information

```

### msgraph excel update

```
Description:
  Update cell values in a range

Usage:
  msgraph excel update <itemId> [options]

Arguments:
  <itemId>  Drive item ID of the Excel file

Options:
  --sheet <sheet> (REQUIRED)    Worksheet name
  --range <range> (REQUIRED)    Range address (e.g. A1:B2)
  --values <values> (REQUIRED)  JSON 2D array of values (e.g. '[["A",1],["B",2]]')
  -?, -h, --help                Show help and usage information

```

### msgraph excel append

```
Description:
  Append rows to a table

Usage:
  msgraph excel append <itemId> [options]

Arguments:
  <itemId>  Drive item ID of the Excel file

Options:
  --sheet <sheet> (REQUIRED)    Worksheet name
  --table <table> (REQUIRED)    Table name
  --values <values> (REQUIRED)  JSON 2D array of row values
  -?, -h, --help                Show help and usage information

```

---

## msgraph docs

```
Description:
  Document operations (Word, PowerPoint)

Usage:
  msgraph docs [command] [options]

Options:
  -?, -h, --help  Show help and usage information

Commands:
  export <itemId>  Export a document to another format
  cat <itemId>     Extract text content as markdown

```

### msgraph docs export

```
Description:
  Export a document to another format

Usage:
  msgraph docs export <itemId> [options]

Arguments:
  <itemId>  Drive item ID of the document

Options:
  --format <format>       Export format (default: pdf)
  --out <out> (REQUIRED)  Output file path
  -?, -h, --help          Show help and usage information

```

### msgraph docs cat

```
Description:
  Extract text content as markdown

Usage:
  msgraph docs cat <itemId> [options]

Arguments:
  <itemId>  Drive item ID of the document

Options:
  --out-dir <out-dir>  Directory to save extracted images
  -?, -h, --help       Show help and usage information

```

---

## msgraph config

```
Description:
  View and modify configuration

Usage:
  msgraph config [command] [options]

Options:
  -?, -h, --help  Show help and usage information

Commands:
  path               Show the configuration file path
  list               Show all configuration values
  get <key>          Get a configuration value
  set <key> <value>  Set a configuration value

```

### msgraph config path

```
Description:
  Show the configuration file path

Usage:
  msgraph config path [options]

Options:
  -?, -h, --help  Show help and usage information

```

### msgraph config list

```
Description:
  Show all configuration values

Usage:
  msgraph config list [options]

Options:
  -?, -h, --help  Show help and usage information

```

### msgraph config get

```
Description:
  Get a configuration value

Usage:
  msgraph config get <key> [options]

Arguments:
  <key>  Configuration key name

Options:
  -?, -h, --help  Show help and usage information

```

### msgraph config set

```
Description:
  Set a configuration value

Usage:
  msgraph config set <key> <value> [options]

Arguments:
  <key>    Configuration key name
  <value>  Value to set

Options:
  -?, -h, --help  Show help and usage information

```

---

## msgraph completions

```
Description:
  Generate shell completion scripts

Usage:
  msgraph completions [command] [options]

Options:
  -?, -h, --help  Show help and usage information

Commands:
  bash  Generate bash completion script
  zsh   Generate zsh completion script
  fish  Generate fish completion script

```

### msgraph completions bash

```
Description:
  Generate bash completion script

Usage:
  msgraph completions bash [options]

Options:
  -?, -h, --help  Show help and usage information

```

### msgraph completions zsh

```
Description:
  Generate zsh completion script

Usage:
  msgraph completions zsh [options]

Options:
  -?, -h, --help  Show help and usage information

```

### msgraph completions fish

```
Description:
  Generate fish completion script

Usage:
  msgraph completions fish [options]

Options:
  -?, -h, --help  Show help and usage information

```

---

