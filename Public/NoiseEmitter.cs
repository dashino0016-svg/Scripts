using UnityEngine;

public class NoiseEmitter : MonoBehaviour
{
    [Header("Noise Radius")]
    public float walkNoiseRadius = 4f;
    public float runNoiseRadius = 8f;
    public float sprintNoiseRadius = 12f;

    public float CurrentNoiseRadius { get; private set; }

    PlayerMove playerMove;
    PlayerController playerController;
    EnemyMove enemyMove;

    void Awake()
    {
        playerMove = GetComponent<PlayerMove>();
        playerController = GetComponent<PlayerController>();
        enemyMove = GetComponent<EnemyMove>();
    }

    void Update()
    {
        CurrentNoiseRadius = 0f;

        if (playerMove != null)
        {
            if (!playerMove.IsMoving)
                return;

            bool crouching = playerController != null && playerController.IsCrouching;
            if (crouching && playerMove.IsWalking)
                return;

            if (playerMove.IsSprinting)
                CurrentNoiseRadius = sprintNoiseRadius;
            else if (playerMove.IsRunning)
                CurrentNoiseRadius = runNoiseRadius;
            else
                CurrentNoiseRadius = walkNoiseRadius;

            return;
        }

        if (enemyMove != null)
        {
            CurrentNoiseRadius = enemyMove.DesiredSpeedLevel switch
            {
                1 => walkNoiseRadius,
                2 => runNoiseRadius,
                3 => sprintNoiseRadius,
                _ => 0f
            };
        }
    }
}
