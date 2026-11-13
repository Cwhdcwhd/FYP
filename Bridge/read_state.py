import json
from copy import deepcopy
from typing import Any

from bridge_client import BridgeClient


BRIDGE_URL = "http://127.0.0.1:8765"


class State:
    """Stores one bridge-state snapshot and provides readers for it."""

    def __init__(self, bridge: BridgeClient):
        self.bridge = bridge
        self.data: dict[str, Any] = {}

    def refresh(self) -> None:
        """
        Fetch and store the latest state.

        Readers use this stored snapshot without making HTTP requests.
        If the request fails, the previously stored snapshot is retained.
        """
        latest = self.bridge.get_state()
        self.data = latest

    # ---------- Full snapshot ----------

    def get_all(self) -> dict[str, Any]:
        """Return a copy of the full stored snapshot."""
        return deepcopy(self.data)

    # ---------- Combat information ----------

    def get_phase(self) -> str:
        return self.data.get("phase", "unknown")

    def is_in_combat(self) -> bool:
        return self.data.get("in_combat", False)

    def get_round(self) -> int:
        return self.data.get("round", 0)

    def get_turn(self) -> int:
        return self.data.get("turn", 0)

    def get_current_side(self) -> str:
        return self.data.get("current_side", "")

    def is_player_turn(self) -> bool:
        return self.data.get("is_player_turn", False)

    def get_player_turn_phase(self) -> str:
        return self.data.get("player_turn_phase", "")

    # ---------- Player ----------

    def get_player(self) -> dict[str, Any]:
        return deepcopy(self.data.get("player", {}))

    def get_hp(self) -> int:
        return self.data.get("player", {}).get("hp", 0)

    def get_max_hp(self) -> int:
        return self.data.get("player", {}).get("max_hp", 0)

    def get_block(self) -> int:
        return self.data.get("player", {}).get("block", 0)

    def get_energy(self) -> int:
        return self.data.get("player", {}).get("energy", 0)

    def get_max_energy(self) -> int:
        return self.data.get("player", {}).get("max_energy", 0)

    def get_player_powers(self) -> list[dict[str, Any]]:
        return deepcopy(
            self.data.get("player", {}).get("powers", [])
        )

    # ---------- Cards and piles ----------

    def get_hand(self) -> list[dict[str, Any]]:
        return deepcopy(self.data.get("hand", []))

    def get_card(self, card_index: int) -> dict[str, Any]:
        """Find a hand card by its exported index."""
        return self._find_by_index(
            self.data.get("hand", []),
            "index",
            card_index,
            "card",
        )

    def get_hand_count(self) -> int:
        return self.data.get("hand_count", 0)

    def get_draw_pile_count(self) -> int:
        return self.data.get("draw_pile_count", 0)

    def get_discard_pile_count(self) -> int:
        return self.data.get("discard_pile_count", 0)

    def get_exhaust_pile_count(self) -> int:
        return self.data.get("exhaust_pile_count", 0)

    # ---------- Potions ----------

    def get_potions(self) -> list[dict[str, Any]]:
        """Return all potion slots, including empty slots."""
        return deepcopy(self.data.get("potions", []))

    def get_potion(self, slot: int) -> dict[str, Any]:
        return self._find_by_index(
            self.data.get("potions", []),
            "slot",
            slot,
            "potion slot",
        )

    # ---------- Relics ----------

    def get_relics(self) -> list[dict[str, Any]]:
        return deepcopy(self.data.get("relics", []))

    # ---------- Enemies ----------

    def get_enemies(self) -> list[dict[str, Any]]:
        return deepcopy(self.data.get("enemies", []))

    def get_enemy(self, enemy_index: int) -> dict[str, Any]:
        return self._find_by_index(
            self.data.get("enemies", []),
            "index",
            enemy_index,
            "enemy",
        )

    def get_enemy_powers(
        self,
        enemy_index: int,
    ) -> list[dict[str, Any]]:
        return self.get_enemy(enemy_index).get("powers", [])

    # ---------- Actions ----------

    def get_legal_actions(self) -> list[dict[str, Any]]:
        """Return the action list currently exported by the bridge."""
        return deepcopy(self.data.get("legal_actions", []))

    # ---------- Display ----------

    def show(self, section: str | None = None) -> None:
        """
        Print the full snapshot or one top-level section.

        Examples:
            state.show()
            state.show("hand")
            state.show("player")
            state.show("enemies")
        """
        if section is None:
            value = self.data
        else:
            if section not in self.data:
                raise KeyError(
                    f"State section '{section}' is unavailable."
                )
            value = self.data[section]

        print(json.dumps(
            value,
            indent=2,
            ensure_ascii=False,
        ))

    # ---------- Internal helper ----------

    @staticmethod
    def _find_by_index(
        items: list[dict[str, Any]],
        key: str,
        index: int,
        label: str,
    ) -> dict[str, Any]:
        if type(index) is not int or index < 0:
            raise ValueError(
                f"{label} index must be a non-negative integer."
            )

        for item in items:
            if item.get(key) == index:
                return deepcopy(item)

        raise IndexError(
            f"No {label} with {key}={index} in the stored snapshot."
        )


if __name__ == "__main__":
    with BridgeClient(BRIDGE_URL) as bridge:
        state = State(bridge)

        # Fetch one snapshot before reading it.
        state.refresh()

        print("Player:")
        state.show("player")

        print("\nHand:")
        state.show("hand")

        print("\nPotions:")
        state.show("potions")

        print("\nRelics:")
        state.show("relics")

        print("\nEnemies:")
        state.show("enemies")