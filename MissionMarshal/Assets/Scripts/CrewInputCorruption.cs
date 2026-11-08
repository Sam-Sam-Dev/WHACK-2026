using System;
using UnityEngine;

/// <summary>
/// One crew's fixed input rules. Does not subscribe to hardware or modify raw values.
/// Keypad coordinates use ComponentManager's grid: (0,0) = physical row 4/column 1.
/// Vector2Int order is (column, row): X goes right and Y goes down.
/// Layout string is TOP row left-to-right, then each row down to the bottom.
/// No coordinate flips or random failures are applied here.
/// Joystick direction may be inverted once by the task; axes are never swapped.
/// </summary>
[Serializable]
public sealed class CrewInputCorruption
{
    [SerializeField] private int crewId;
    [SerializeField] private string keypadLayout;
    [SerializeField] private bool invertJoystick;
    public bool InvertJoystick => invertJoystick;
    public int CrewId => crewId;
    public string KeypadLayout => keypadLayout;

    public CrewInputCorruption(int crewId, string keypadLayout, bool invert = false)
    {
        this.crewId = crewId;
        this.keypadLayout = keypadLayout;
        invertJoystick = invert;
    }

    public static CrewInputCorruption Generate(int crewId, System.Random random)
    {
        char[] symbols = "0123456789ABCDEF".ToCharArray();
        for (int i = symbols.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            char temporary = symbols[i]; symbols[i] = symbols[j]; symbols[j] = temporary;
        }
        return new CrewInputCorruption(crewId, new string(symbols), random.Next(2) != 0);
    }

    public bool TryGetKeypadSymbol(Vector2Int position, out char symbol)
    {
        symbol = '\0';
        if (position.x < 0 || position.x > 3 || position.y < 0 || position.y > 3 ||
            string.IsNullOrEmpty(keypadLayout) || keypadLayout.Length != 16) return false;
        int column = position.x;
        int row = position.y;
        symbol = keypadLayout[row * 4 + column];
        return true;
    }

}
