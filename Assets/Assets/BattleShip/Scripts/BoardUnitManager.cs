using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Burst.CompilerServices;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.UIElements;

public class BoardUnitManager : MonoBehaviour
{
    public int playerSunkShips = 0;
    public TextMeshProUGUI playerScoreText;
    public TextMeshProUGUI enemyScoreText;
    public GameObject fire;
    public GameObject bomb;
    public UIManager uiManager;
    public CamerasController controller;
    public ShipPanelManager shipPanelManager;
    public delegate void BoardPiecePlaced(int id);
    public static event BoardPiecePlaced OnBoardPiecePlaced;
    public GameObject BoardUnitPrefab;
    public GameObject BoardUnitAttackPrefab;
    public GameObject BlockVisualizerPrefab;
    private bool isAttackPhase = false;
    public BoardPlayer boardPlayer;
    public BoardAI boardEnemy;
    public List<Ship> playerShips = new List<Ship>();
    public int enemySunkShips = 0;
    private int originalFontSize;
    public AudioSource audioSource;
    public AudioClip popSoundEffect;
    private List<Vector2Int> missedPositions = new List<Vector2Int>();

    // public int[] ShipSizes = { 2, 3, 3, 4, 5 };
    public int ShipSize = 2;
    public bool Vertical = true;

    [Header("Player Piece Model Prefact Reference")]
    public List<GameObject> boardPiecesPref;

    [Header("----")]
    //public int blockSize = 3;
    //public bool Oerientation = false;

    bool PLACE_BLOCK = true;

    [SerializeField]
    private int currentShipID;

    GameObject tmpHighlight = null;
    RaycastHit tmpHitHighlight;

    GameObject tmpBlockHolder = null;

    private bool OK_TO_PLACE = true;

    [SerializeField]
    private int count = 0;

    bool placeEnemyShips = true;

    GameObject tmpAttackHighlight = null;
    RaycastHit tmpAttackHitHighlight;

    GameObject tmpAttackBlockHolder = null;
    private void OnEnable()
    {
        UIManager.OnChangeShip += UIManager_OnChangeShip;
        UIManager.OnChangeOrientation += UIManager_OnChangeOrientation;
    }

    private void UIManager_OnChangeOrientation(bool Orienation)
    {
        Vertical = !Vertical;
    }

    private void UIManager_OnChangeShip(int id, int size)
    {
        currentShipID = id;
        ShipSize = size;
    }

    private void OnDisable()
    {
        UIManager.OnChangeShip -= UIManager_OnChangeShip;
        UIManager.OnChangeOrientation -= UIManager_OnChangeOrientation;
    }

    // Start is called before the first frame update
    void Start()
    {
        // Initialize the enemy board first
        boardEnemy = new BoardAI(BoardUnitAttackPrefab, BlockVisualizerPrefab);

        originalFontSize = 30;
        // Now initialize the player board with a reference to the enemy board
        boardPlayer = new BoardPlayer(BoardUnitPrefab, boardEnemy);
        boardPlayer.CreatePlayerBoard();
        boardEnemy.SetPlayerBoard(boardPlayer);
        boardEnemy.CreateAiBoard();
        boardEnemy.SetBoardPlayer(this);

        currentShipID = -1;
        ShipSize = 0;
    }

    public enum ScoreType
    {
        Player,
        Enemy
    }
    public void HandleEnemyAttack(BoardUnit targetUnit, Ship hitShip)
    {
        var bombPosition = targetUnit.transform.position;
        bombPosition.y += 10;
        StartCoroutine(EnemyBombDrop(bombPosition));
        if (hitShip != null)
        {
            StartCoroutine(InstantiateFireForPlayerShipVFX(targetUnit.transform.position));
            if (hitShip.IsSunk())
            {
                playerSunkShips++;
                UpdateUI(ScoreType.Enemy);
                Debug.Log($"Player{hitShip.Name} has been sunk!");
                isAttackPhase = false;

               
                // Perform any additional actions needed when a ship is sunk
            }
            else
            {
                Debug.Log($"Player{hitShip.Name} has been hit!");
                isAttackPhase = false;
             
            }
        }
        else
        {
            Debug.Log("Missed all ships.");
            

        }

        // Additional logic (e.g., check if the game is over, switch turns, etc.)
    
}
    public void UpdateUI(ScoreType scoreType)
    {
        TextMeshProUGUI scoreText;
        // Assuming you have a UI Text or similar to show the scores
        switch (scoreType)
        {
            case ScoreType.Player:
                scoreText = playerScoreText;
                playerScoreText.text = $"Player Score: {enemySunkShips}/5";
                break;
            case ScoreType.Enemy:
                scoreText = enemyScoreText;
                enemyScoreText.text = $"Enemy Score: {playerSunkShips}/5";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scoreType), scoreType, null);
        }

        // Trigger the pop-out effect for the updated score
        StopCoroutine(PopOutScore(scoreText, scoreType)); // Stop if already running
        StartCoroutine(PopOutScore(scoreText, scoreType));
        if(enemySunkShips == 5)
        {
            StartCoroutine(LoadEndGameScene("YOU WON!"));
        }
        if(playerSunkShips == 5)
        {
            StartCoroutine(LoadEndGameScene("YOU LOST!"));
        }
    }

    private IEnumerator LoadEndGameScene(string result)
    {
        yield return new WaitForSeconds(3f);
        // Save the result to use in the endgame scene
        PlayerPrefs.SetString("endGameResult", result);
        SceneManager.LoadScene("EndGame"); // Replace with your endgame scene name
    }
    public void StartAttackPlayer()
    {
        uiManager.EnablePieceButtonsForAttackPhase();
        isAttackPhase = true;
    }
    void Update()
    {
        if (isAttackPhase)
        {
            HandlePlayerAttack();
        }
        if (IsBusy)
            return;

        if (count < 5)
        {
            PlacePlayerPieces();
        }
        else 
        {
            if (placeEnemyShips)
            {
                placeEnemyShips = false;
                boardEnemy.PlaceShips();
                StartAttackPlayer();
                
              
            }
        }
        
       
    }
    public Ship CheckHit(int row, int col)
    {
        foreach (var ship in playerShips)
        {
            if (ship.Positions.Contains(new Vector2Int(row, col)))
            {
                ship.RegisterHit();
                return ship;
            }
        }
        return null; // No ship at this position
    }

    private void HandlePlayerAttack()
    {
        Debug.Log("HandlePlayerAttack called");
        controller.SwitchToEnemyView();
        if (Input.GetMouseButtonDown(0)) // Check for left mouse button click
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit))
            {
                if (hit.collider.CompareTag("EnemyBoardUnit"))
                {
                    // Check if the raycast hit an enemy board unit
                    BoardUnit enemyUnit = hit.transform.GetComponent<BoardUnit>();
                    Vector2Int attackPosition = new Vector2Int(enemyUnit.row, enemyUnit.col);
                    if (enemyUnit != null && !enemyUnit.hit) // Additional check to ensure unit hasn't already been hit
                    {
                        Debug.Log("Clicked on an enemy board unit!");
                        enemyUnit.ProcessHit(); // Process the hit on the enemy unit

                        // Instantiate the bomb at the clicked position
                        Vector3 bombPosition = new Vector3(hit.point.x, 9, hit.point.z); // Adjust the Y value as needed
                        if (!missedPositions.Contains(attackPosition))
                        {
                            Instantiate(bomb, bombPosition, Quaternion.identity);
                        }


                        // Check if a ship has been hit and if it has sunk
                        Ship hitShip = boardEnemy.CheckHit(enemyUnit.row, enemyUnit.col);

                        if (hitShip != null)
                        {
                            StartCoroutine(InstantiateFireVFX(hit.transform.position));
                            if (hitShip.IsSunk())
                            {
                                enemySunkShips++;
                                UpdateUI(ScoreType.Player);
                                Debug.Log($"{hitShip.Name} has been sunk!");
                                shipPanelManager.UpdateShipPanel(hitShip.Name, true);
                                isAttackPhase = false;

                                StartCoroutine(StartEnemyAttack());
                                // Perform any additional actions needed when a ship is sunk
                            }
                            else 
                            {
                                Debug.Log($"{hitShip.Name} has been hit!");
                                isAttackPhase = false;
                                StartCoroutine(StartEnemyAttack());
                            }
                        }
                        else if (!(missedPositions.Contains(attackPosition)))
                        {
                            Debug.Log("Missed all ships.");
                            isAttackPhase = false;
                            missedPositions.Add(attackPosition);
                            StartCoroutine(StartEnemyAttack());

                        }
                        else if (missedPositions.Contains(attackPosition))
                        {
                            Debug.Log("Please try another target");
                        }

                        // Additional logic (e.g., check if the game is over, switch turns, etc.)
                    }
                    else
                    {
                        Debug.Log("Clicked, but not on an enemy board unit.");
                    }
                }
                else
                {
                    Debug.Log("Raycast didn't hit anything when clicked.");
                }
            }
        }
    }
    private IEnumerator EnemyBombDrop(Vector3 bombPosition)
    {
        yield return new WaitForSeconds(1.2f);
        Instantiate(bomb, bombPosition, Quaternion.identity);
    }
    private IEnumerator StartEnemyAttack()
    {
        // Wait for a second
        yield return new WaitForSeconds(3.5f);
        controller.SwitchToPlayerView();
        boardEnemy.EnemyAttack();
    }
    public void PlayerTurn()
    {
        StartCoroutine(SwitchToPlayerTurn());
    }
    public IEnumerator SwitchToPlayerTurn()
    {
        yield return new WaitForSeconds(3.5f);
        isAttackPhase = true;
    }
    private IEnumerator InstantiateFireVFX(Vector3 position)
    {
        // Wait for a second
        yield return new WaitForSeconds(1.2f);

        // Instantiate the fire VFX
        Instantiate(fire, position, Quaternion.identity);
    }
    private IEnumerator InstantiateFireForPlayerShipVFX(Vector3 position)
    {
        // Wait for a second
        yield return new WaitForSeconds(2.3f);

        // Instantiate the fire VFX
        Instantiate(fire, position, Quaternion.identity);
    }

    private void PlacePlayerPieces()
    {
        if (currentShipID == -1)
        {
            return; // No ship selected, so return early
        }
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        if (Input.mousePosition != null)
        {
            if (Physics.Raycast(ray, out tmpHitHighlight, 100))
            {
                BoardUnit tmpUI = tmpHitHighlight.transform.GetComponent<BoardUnit>();
                if (tmpHitHighlight.transform.tag.Equals("PlayerBoardUnit") && !tmpUI.occupied)
                {
                    BoardUnit boardData = boardPlayer.board[tmpUI.row, tmpUI.col].transform.GetComponent<BoardUnit>();

                    if (tmpHighlight != null)
                    {
                        if (boardData.occupied)
                            tmpHighlight.GetComponent<Renderer>().material.color = Color.red;
                        else
                            tmpHighlight.GetComponent<Renderer>().material.color = Color.white;
                    }

                    if (tmpBlockHolder != null)
                    {
                        Destroy(tmpBlockHolder);
                    }

                    if (PLACE_BLOCK)
                    {
                        tmpBlockHolder = new GameObject();
                        OK_TO_PLACE = true;

                        // Visualization logic for placing the ship...
                        for (int i = 0; i < ShipSize; i++)
                        {
                            int row = Vertical ? tmpUI.row : tmpUI.row + i;
                            int col = Vertical ? tmpUI.col + i : tmpUI.col;

                            if (row >= 10 || col >= 10) continue; // Skip if outside board bounds

                            GameObject visual = GameObject.Instantiate(BlockVisualizerPrefab, new Vector3(row, BlockVisualizerPrefab.transform.position.y, col), BlockVisualizerPrefab.transform.rotation);
                            BoardUnit bpUI = boardPlayer.board[row, col].GetComponentInChildren<BoardUnit>();

                            if (!bpUI.occupied)
                            {
                                visual.GetComponent<Renderer>().material.color = Color.gray; // okay to place
                            }
                            else
                            {
                                visual.GetComponent<Renderer>().material.color = Color.yellow; // not ok
                                OK_TO_PLACE = false;
                            }

                            visual.transform.parent = tmpBlockHolder.transform;
                        }
                    }
                }
            }
        }

        if (Input.GetMouseButton(0))
        {
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, 100))
            {
                if (hit.transform.tag.Equals("PlayerBoardUnit"))
                {
                    BoardUnit tmpUI = hit.transform.GetComponentInChildren<BoardUnit>();

                    if (PLACE_BLOCK && OK_TO_PLACE && CanPlaceShip(tmpUI.row, tmpUI.col, Vertical, ShipSize))
                    {
                        Ship newShip = new Ship($"Ship{currentShipID}", ShipSize);
                        // Place the ship on the board
                        for (int i = 0; i < ShipSize; i++)
                        {
                            int row = Vertical ? tmpUI.row : tmpUI.row + i;
                            int col = Vertical ? tmpUI.col + i : tmpUI.col;

                            GameObject sB = boardPlayer.board[row, col];
                            BoardUnit bu = sB.transform.GetComponentInChildren<BoardUnit>();
                            bu.occupied = true;
                          //  bu.GetComponent<MeshRenderer>().material.color = Color.green;
                            boardPlayer.board[row, col] = sB;
                            newShip.AddPosition(row, col);
                        }

                        CheckWhichShipWasPlaced(tmpUI.row, tmpUI.col); // Existing logic for checking which ship is placed
                        playerShips.Add(newShip);
                        OK_TO_PLACE = true;
                        tmpHighlight = null;
                    }
                    if (count >= 5 && tmpBlockHolder != null)
                    {
                        Destroy(tmpBlockHolder);
                    }
                }
            }
        }
    }

    // New method to check if a ship can be placed
    private bool CanPlaceShip(int startRow, int startCol, bool vertical, int shipSize)
    {
        for (int i = 0; i < shipSize; i++)
        {
            int checkRow = vertical ? startRow : startRow + i;
            int checkCol = vertical ? startCol + i : startCol;

            if (checkRow >= 10 || checkCol >= 10) return false; // Check for board bounds

            BoardUnit unit = boardPlayer.board[checkRow, checkCol].GetComponentInChildren<BoardUnit>();
            if (unit.occupied) return false; // Check if the position is already occupied
        }
        return true; // All positions are free
    }
    private void CheckWhichShipWasPlaced(int row, int col)
    {
        Debug.Log("Attempting to place ship with ID: " + currentShipID + " at position: " + row + ", " + col);
        switch (currentShipID)
        {
            case 0:
                {
                    if (!Vertical)
                    {
                        Debug.Log($"id is {currentShipID}");
                        
                        GameObject testingVisual = GameObject.Instantiate(boardPiecesPref[currentShipID],
                                                   new Vector3(row + 2 , boardPiecesPref[currentShipID].transform.position.y,col),
                                                   boardPiecesPref[currentShipID].transform.rotation) as GameObject;
                        testingVisual.transform.RotateAround(testingVisual.transform.position, Vector3.up, 90.0f);
                   
                        if (testingVisual == null)
                        {
                            Debug.LogError("Failed to instantiate Aircraft Carrier prefab.");
                        }
                    }
                    else
                    {
                        GameObject testingVisual = GameObject.Instantiate(boardPiecesPref[currentShipID],
                                                new Vector3(row, boardPiecesPref[currentShipID].transform.position.y, col +2),
                                                boardPiecesPref[currentShipID].transform.rotation) as GameObject;
                    }
                    count++;
                    break;
                }
            case 1:
                {
                    if (!Vertical)
                    {
                        Debug.Log($"id is {currentShipID}");
                        // place it as vertical
                        GameObject testingVisual = GameObject.Instantiate(boardPiecesPref[currentShipID],
                                                   new Vector3(row + 1.5f, boardPiecesPref[currentShipID].transform.position.y, col),
                                                   boardPiecesPref[currentShipID].transform.rotation) as GameObject;
                        testingVisual.transform.RotateAround(testingVisual.transform.position, Vector3.up, 90.0f);
                    }
                    else
                    {
                        GameObject testingVisual = GameObject.Instantiate(boardPiecesPref[currentShipID],
                                                new Vector3(row, boardPiecesPref[currentShipID].transform.position.y, col + 1.5f),
                                                boardPiecesPref[currentShipID].transform.rotation) as GameObject;
                    }
                    count++;
                    break;
                }
            case 2:
                {
                    if (!Vertical)
                    {
                        Debug.Log($"id is {currentShipID}");
                        // place it as vertical
                        GameObject testingVisual = GameObject.Instantiate(boardPiecesPref[currentShipID],
                                                   new Vector3(row + 1, boardPiecesPref[currentShipID].transform.position.y, col),
                                                   boardPiecesPref[currentShipID].transform.rotation) as GameObject;
                        testingVisual.transform.RotateAround(testingVisual.transform.position, Vector3.up, 90.0f);
                    }
                    else
                    {
                        GameObject testingVisual = GameObject.Instantiate(boardPiecesPref[currentShipID],
                                                new Vector3(row, boardPiecesPref[currentShipID].transform.position.y, col + 1),
                                                boardPiecesPref[currentShipID].transform.rotation) as GameObject;
                    }
                    count++;
                    break;
                }
            case 3:
                {
                    if (!Vertical)
                    {
                        Debug.Log($"id is {currentShipID}");
                        // place it as vertical
                        GameObject testingVisual = GameObject.Instantiate(boardPiecesPref[currentShipID],
                                                   new Vector3(row + 1, boardPiecesPref[currentShipID].transform.position.y, col),
                                                   boardPiecesPref[currentShipID].transform.rotation) as GameObject;
                        testingVisual.transform.RotateAround(testingVisual.transform.position, Vector3.up, 90.0f);
                    }
                    else
                    {
                        GameObject testingVisual = GameObject.Instantiate(boardPiecesPref[currentShipID],
                                                new Vector3(row, boardPiecesPref[currentShipID].transform.position.y, col + 1),
                                                boardPiecesPref[currentShipID].transform.rotation) as GameObject;
                    }
                    count++;
                    break;
                }
            case 4:
                {
                    if (!Vertical)
                    {
                        Debug.Log($"id is {currentShipID}");
                        // place it as vertical
                        GameObject testingVisual = GameObject.Instantiate(boardPiecesPref[currentShipID],
                                                   new Vector3(row + 0.5f, boardPiecesPref[currentShipID].transform.position.y, col),
                                                   boardPiecesPref[currentShipID].transform.rotation) as GameObject;
                        testingVisual.transform.RotateAround(testingVisual.transform.position, Vector3.up, 90.0f);
                    }
                    else
                    {
                        GameObject testingVisual = GameObject.Instantiate(boardPiecesPref[currentShipID],
                                                new Vector3(row, boardPiecesPref[currentShipID].transform.position.y, col + 0.5f),
                                                boardPiecesPref[currentShipID].transform.rotation) as GameObject;
                    }
                    count++;
                    break;
                }

        }
        OnBoardPiecePlaced?.Invoke(currentShipID);
        StartCoroutine(Wait4Me(0.5f));

        // clear internal data
        currentShipID = -1;
        ShipSize = 0;
    }
    private IEnumerator PopOutScore(TextMeshProUGUI scoreText, ScoreType scoreType)
    {
        int popFontSize = originalFontSize + 50; // The font size to pop out to, adjust as needed
        float animationTime = 1.3f; // Duration of the animation, adjust as needed
        float time = 0;
        audioSource.PlayOneShot(popSoundEffect);
        // Increase font size instantly
        scoreText.fontSize = popFontSize;
        
        // Animate font size back to original
        while (time < animationTime)
        {
            time += Time.deltaTime;
            float t = time / animationTime;

            // Lerp font size back to the original font size over time
            scoreText.fontSize = (int)Mathf.Lerp(popFontSize, originalFontSize, t);

            yield return null; // Wait until the next frame
        }

        // Ensure the font size is set to the original size when the animation is done
        scoreText.fontSize = originalFontSize;
    }
    public static bool IsBusy = false;
    IEnumerator Wait4Me(float seconds = 0.5f)
    {
        IsBusy = true;
        //Debug.Log("I AM IN WAIT BEFORE WAIT");
        yield return new WaitForSeconds(seconds);
        //Debug.Log("I AM IN WAIT AFTER WAIT");
        IsBusy = false;
    }
}
