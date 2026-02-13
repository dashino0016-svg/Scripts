using UnityEngine;

public static class EnemyRespawnService
{
    public static void RespawnAllEnemiesToHome()
    {
        EnemyController[] enemies = Object.FindObjectsOfType<EnemyController>(true);
        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyController enemy = enemies[i];
            if (enemy == null) continue;
            enemy.RespawnToHome();
        }
    }
}
