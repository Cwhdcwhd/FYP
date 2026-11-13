from typing import Any

import requests


class BridgeClient:
    """Python interface to the existing C# HTTP bridge."""

    def __init__(
        self,
        base_url: str = "http://127.0.0.1:8765",
        timeout: float = 5.0,
    ):
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout
        self.session = requests.Session()

    def get_state(self) -> dict[str, Any]:
        """Get the latest state exported by the C# mod."""
        response = self.session.get(
            f"{self.base_url}/state",
            timeout=self.timeout,
        )
        response.raise_for_status()
        return response.json()

    def send_action(
        self,
        action: dict[str, Any],
    ) -> dict[str, Any]:
        """
        Submit an action to the bridge.

        The current server acknowledges queue receipt, not successful
        execution or completion. Do not automatically retry POST requests.
        """
        response = self.session.post(
            f"{self.base_url}/action",
            json=action,
            timeout=self.timeout,
        )
        response.raise_for_status()
        return response.json()

    def play_card(
        self,
        card_index: int,
        target_index: int | None = None,
        *,
        target: str | None = None,
    ) -> dict[str, Any]:
        """
        Play a card at its current hand index.

        Enemy target: target_index=0
        Player target: target="self"
        No target: omit both target arguments
        """
        self._validate_index(card_index, "card_index")

        action: dict[str, Any] = {
            "type": "play_card",
            "card_index": card_index,
        }

        self._add_target(action, target_index, target)
        return self.send_action(action)

    def use_potion(
        self,
        potion_slot: int,
        target_index: int | None = None,
        *,
        target: str | None = None,
    ) -> dict[str, Any]:
        """Use a potion from its slot, with an optional target."""
        self._validate_index(potion_slot, "potion_slot")

        action: dict[str, Any] = {
            "type": "use_potion",
            "potion_slot": potion_slot,
        }

        self._add_target(action, target_index, target)
        return self.send_action(action)

    def end_turn(self) -> dict[str, Any]:
        """Request the end of the player's turn."""
        return self.send_action({"type": "end_turn"})

    def get_hand(self) -> list[dict[str, Any]]:
        return self.get_state().get("hand", [])

    def get_potions(self) -> list[dict[str, Any]]:
        return self.get_state().get("potions", [])

    def get_enemies(self) -> list[dict[str, Any]]:
        return self.get_state().get("enemies", [])

    def close(self) -> None:
        self.session.close()

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc_value, traceback):
        self.close()

    @staticmethod
    def _validate_index(value: int, name: str) -> None:
        if type(value) is not int or value < 0:
            raise ValueError(f"{name} must be a non-negative integer.")

    @staticmethod
    def _add_target(
        action: dict[str, Any],
        target_index: int | None,
        target: str | None,
    ) -> None:
        if target_index is not None and target is not None:
            raise ValueError(
                "Use target_index or target, not both."
            )

        if target_index is not None:
            BridgeClient._validate_index(
                target_index,
                "target_index",
            )
            action["target_index"] = target_index

        elif target is not None:
            if target not in ("self", "none"):
                raise ValueError(
                    "target must be 'self' or 'none'."
                )
            action["target"] = target