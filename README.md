## Python test scripts

Two Python scripts are included for manually testing the bridge:

- `read_state.py`: reads and prints the current game or combat state.
- `send_action.py`: sends an action to the bridge.

Both scripts use the bridge address:

```text
http://127.0.0.1:5000
```

### `read_state.py`

`read_state.py` sends a `GET` request to `/state`.

It prints the HTTP status code and the JSON state returned by the bridge.

Run it with:

```bash
python read_state.py
```

Use this script to verify that:

- The bridge is running.
- Python can connect to the bridge.
- The current combat state is being serialized correctly.
- State changes after an action.

### `send_action.py`

`send_action.py` sends a `POST` request to `/action`.

It supports two actions:

| Command | Action sent |
|---|---|
| `python send_action.py --end-turn` | Ends the current player turn |
| `python send_action.py --play-card 0` | Plays the first card in hand |
| `python send_action.py --play-card 1 --target 0` | Plays the second card in hand and targets the first enemy |

Card and enemy indexes begin at `0`.

The `--target` option is used only when the selected card requires a target. It is ignored for cards that do not require one.

### Testing workflow

1. Start Slay the Spire 2.
2. Confirm that `MyRLBridge` and BaseLib are loaded.
3. Enter a combat encounter.
4. Read the current state:

```bash
python read_state.py
```

5. Send an action:

```bash
python send_action.py --end-turn
```

6. Read the updated state:

```bash
python read_state.py
```

### Expected result

After `send_action.py --end-turn`, the bridge should report that the action was accepted. Running `read_state.py` again should show that the game has advanced beyond the player turn, such as a change in turn number, phase, energy, hand, or enemy state.

The exact visible changes depend on the encounter, the current turn, and the implementation of `StateCapture.cs`.

### Troubleshooting

If either script cannot connect:

- Confirm Slay the Spire 2 is running.
- Confirm that `MyRLBridge` is loaded.
- Confirm that BaseLib is installed and loaded.
- Confirm that the bridge is listening on port `5000`.
- Confirm that Python is using the same port as the bridge.
- Confirm that the game is currently in combat before sending a combat action.
