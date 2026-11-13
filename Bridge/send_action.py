from bridge_client import BridgeClient

# Import the same URL so both scripts use the same bridge.
from read_state import BRIDGE_URL


def send_test_action(bridge: BridgeClient) -> None:
    """
    Send one manually selected action.

    Uncomment exactly one action below.
    Read the latest state before choosing indices.
    """

    # Play hand card 0 on enemy 0:
    # print(bridge.play_card(0, target_index=0))

    # Play hand card 0 without a target:
    # print(bridge.play_card(0))

    # Play hand card 0 targeting yourself:
    # print(bridge.play_card(0, target="self"))

    # Use potion slot 0 on enemy 0:
    # print(bridge.use_potion(0, target_index=0))

    # Use potion slot 0 on yourself:
    # print(bridge.use_potion(0, target="self"))

    # Use an untargeted potion:
    # print(bridge.use_potion(0))

    # End your turn:
    # print(bridge.end_turn())

    # Keeps this function valid while all actions are commented out.
    pass


if __name__ == "__main__":
    with BridgeClient(BRIDGE_URL) as bridge:
        send_test_action(bridge)