using Fusion;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// ✅ AJOUTE ces méthodes à ton TurnManager existant
/// Cette classe montre comment intégrer la logique de fin de partie
public partial class TurnManager
{
    // Cette méthode doit être appelée par BallAimController quand une bille meurt
    public void CheckGameEnd()
    {
        // if (!HasInputAuthority) return;

        // ✅ Obtenir le GameSpawner pour lister les billes de chaque joueur
        GameSpawner spawner = FindObjectOfType<GameSpawner>();
        if (spawner == null)
        {
            Debug.LogError("[TurnManager] GameSpawner not found!");
            return;
        }

        // ✅ Vérifier combien de billes vivantes chaque joueur a
        Dictionary<PlayerRef, int> aliveBallsPerPlayer = new Dictionary<PlayerRef, int>();

        // Parcourir toutes les billes dans la scène
        BallAimController[] allBalls = FindObjectsOfType<BallAimController>();

        foreach (BallAimController ball in allBalls)
        {
            NetworkObject netObj = ball.GetComponent<NetworkObject>();
            if (netObj == null) continue;

            PlayerRef owner = netObj.InputAuthority;

            if (!aliveBallsPerPlayer.ContainsKey(owner))
            {
                aliveBallsPerPlayer[owner] = 0;
            }

            // ✅ Compter seulement les billes vivantes
            if (ball.IsAlive)
            {
                aliveBallsPerPlayer[owner]++;
            }
        }

        Debug.Log("[TurnManager] État des billes:");
        foreach (var kvp in aliveBallsPerPlayer)
        {
            Debug.Log($"  Joueur {kvp.Key.PlayerId}: {kvp.Value} billes vivantes");
        }

        // ✅ Vérifier si un joueur (ou plusieurs) n'a plus de billes vivantes
        int playersWithNoBalls = 0;
        foreach (var kvp in aliveBallsPerPlayer)
        {
            if (kvp.Value == 0)
            {
                playersWithNoBalls++;
            }
        }

        // ✅ CAS D'ÉGALITÉ : Les deux joueurs n'ont plus de billes
        if (playersWithNoBalls >= 2)
        {
            Debug.Log("[TurnManager] 🤝 ÉGALITÉ! Les deux joueurs n'ont plus de billes!");
            EndGameDraw();
            return;
        }

        // ✅ UN JOUEUR N'A PLUS DE BILLES : L'autre a gagné
        if (playersWithNoBalls == 1)
        {
            foreach (var kvp in aliveBallsPerPlayer)
            {
                if (kvp.Value == 0)
                {
                    EndGameWin(kvp.Key);
                    return;
                }
            }
        }
    }

    // ✅ FIN DE PARTIE AVEC GAGNANT
    private void EndGameWin(PlayerRef loser)
    {
        // Trouver le gagnant (l'autre joueur)
        BallAimController[] allBalls = FindObjectsOfType<BallAimController>();
        PlayerRef winner = PlayerRef.None;

        foreach (BallAimController ball in allBalls)
        {
            NetworkObject netObj = ball.GetComponent<NetworkObject>();
            if (netObj != null && netObj.InputAuthority != loser && ball.IsAlive)
            {
                winner = netObj.InputAuthority;
                break;
            }
        }

        Debug.Log($"[TurnManager] 🎊 FIN DE PARTIE! Gagnant: Joueur {winner.PlayerId}, Perdant: Joueur {loser.PlayerId}");

        // ✅ Arrêter le gameplay
        IsTurnBased = false;
        CurrentState = TurnState.Finished;

        // ✅ Recharger la scène directement
        StartCoroutine(ReloadSceneAfterDelay(result: "WIN", winner: winner.PlayerId, loser: loser.PlayerId));
    }

    // ✅ FIN DE PARTIE EN ÉGALITÉ
    private void EndGameDraw()
    {
        Debug.Log($"[TurnManager] 🤝 MATCH NUL! Les deux joueurs n'ont plus de billes en même temps!");

        // ✅ Arrêter le gameplay
        IsTurnBased = false;
        CurrentState = TurnState.Finished;

        // ✅ Recharger la scène directement
        StartCoroutine(ReloadSceneAfterDelay(result: "DRAW", winner: -1, loser: -1));
    }

    // ✅ Recharger la scène avec un petit délai pour voir les animations
    private IEnumerator ReloadSceneAfterDelay(string result, int winner = -1, int loser = -1)
    {
        // Attendre 2 secondes pour que les animations de chute aient le temps de jouer
        yield return new WaitForSeconds(2f);

        if (result == "WIN")
        {
            Debug.Log($"[TurnManager] 🔄 Reloading scene... Gagnant: Joueur {winner}");
        }
        else if (result == "DRAW")
        {
            Debug.Log($"[TurnManager] 🔄 Reloading scene... Match nul!");
        }

        // ✅ Recharger la scène actuelle EN PASSANT PAR FUSION
        // (SceneManager.LoadScene() casse la synchro réseau : les autres clients
        // se retrouvent avec des NetworkObjects dont Spawned() n'a pas encore
        // été appelé -> InvalidOperationException sur les propriétés [Networked])
        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        Runner.LoadScene(SceneRef.FromIndex(currentSceneIndex));
    }

    private void ShowGameOverUI(PlayerRef winner, PlayerRef loser)
    {
        // ✅ À implémenter : afficher un écran de fin de partie avec le gagnant
        // Pour l'instant, simple log
        Debug.Log($"[TurnManager] Afficher l'écran de fin: Joueur {winner.PlayerId} a gagné!");

        // Exemple : tu peux activate un Canvas avec le résultat
        // var gameOverPanel = FindObjectOfType<GameOverPanel>();
        // if (gameOverPanel != null)
        // {
        //     gameOverPanel.ShowResult(winner);
        // }
    }
}
