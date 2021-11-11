using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using PlayingCards;
using Microsoft.Win32;
using System.Reflection;
using System.IO;
using System.Diagnostics;
using GameStatistics;

/*
 * Primary class defines the partial class of the main window for the
 * Crescent Solitaire game.
 *
 * Author:  M. G. Slack
 * Written: 2013-12-31
 * Version: 1.0.6.0
 *
 * ----------------------------------------------------------------------------
 * 
 * Updated: 2014-01-13 - Disabled the 'shuffle' button when game is won.
 *          2014-01-15 - Added code to enable 'undo' functionality.
 *          2014-01-16 - Changed slot = 0 or 1 to slot = FIRST or NEXT.
 *          2014-01-17 - Fixed points if undo moves back king or ace from foundations.
 *          2014-01-24 - Fixed a check in the 'CheckWon' method that ignored the
 *                       last pile during the check (was NUM_PILES-1 - stupid c-n-p).
 *          2014-03-10 - Changed up scoring a bit, keep a 'highest' score (persisted),
 *                       a high score for the current game and added 10 points for
 *                       each 'shuffle' left.
 *          2014-03-13 - Fixed a bug in the accumulation of the shuffle bonus points.
 *          2014-03-20 - Added the game statistics class to track various statistics
 *                       (games won, lost, etc.). Highest score gotten is managed by
 *                       the game statistics class.
 *
 */
namespace CrescentSolitaire
{
    public partial class MainWin : Form
    {
        private const int NUM_PILES = 16;
        private const int NUM_FOUNDATIONS = 4;
        private const int MAX_IN_PILE = 30;
        private const int MAX_IN_FOUNDATION = 13;
        private const int MAX_SHUFFLES = 3;
        private const int FOUND_FINISH_BONUS = 3;
        private const int SHUFFLE_BONUS = 10;

        private const string HTML_HELP_FILE = "CrescentSolitaire_help.html";

        #region Registry Name and Key Consts
        private const string REG_NAME = @"HKEY_CURRENT_USER\Software\Slack and Associates\Games\CrescentSolitaire";
        private const string REG_KEY1 = "PosX";
        private const string REG_KEY2 = "PosY";
        private const string REG_KEY3 = "CardBack";
        private const string REG_KEY4 = "ShufflePiles";
        private const string REG_KEY5 = "ShowCardBack";
        #endregion

        #region Window Fields (variables)
        private PlayingCardImage images = new PlayingCardImage();
        private CardDeck cards = new CardDeck(NumberOfDecks.Two_Deck);
        private CardBacks cardBack = CardBacks.Spheres;
        private CardPlaceholders placeholder = CardPlaceholders.RedX;
        private int shufflesDone = 0, highScore = 0, curScore = 0;
        private bool shufflePiles = false, showCardBack = false;
        private Statistics stats = new Statistics(REG_NAME);

        // card piles (as card hands)
        private CardHand[] cardPiles = new CardHand[NUM_PILES];
        // foundations (as card hands)
        private CardHand[] downFoundations = new CardHand[NUM_FOUNDATIONS]; // king to ace
        private CardHand[] upFoundations = new CardHand[NUM_FOUNDATIONS];   // ace to king

        private PictureBox[] pileDisplay = new PictureBox[NUM_PILES];
        private PictureBox[] dfDisplay = new PictureBox[NUM_FOUNDATIONS];
        private PictureBox[] ufDisplay = new PictureBox[NUM_FOUNDATIONS];
        #endregion

        #region Drag-n-Drop Fields (variables)
        private bool cardImageDragging = false;
        private bool draggedFromFoundation = false;
        private PictureBox dragStartPB;
        private CardHand dragStartFoundOrPile;
        private bool dragStarting = false;
        private bool useCustomCursors = false;
        private Cursor cardMoveCursor;
        private Cursor cardNoneCursor;
        #endregion

        #region Undo Support Fields (variables) and Structs
        private enum UndoTargets { Pile, DownFoundation, UpFoundation };
        private struct Undo
        {
            public UndoTargets from, to;
            public int fromIdx, toIdx;
        }
        private Undo scratch;
        private Stack<Undo> undos = new Stack<Undo>();
        #endregion

        // --------------------------------------------------------------------

        #region Private Methods

        private void LoadFromRegistry()
        {
            int winX = -1, winY = -1, cardB = (int) CardBacks.Spheres;
            string tmpBool = "";

            try {
                winX = (int) Registry.GetValue(REG_NAME, REG_KEY1, winX);
                winY = (int) Registry.GetValue(REG_NAME, REG_KEY2, winY);
                cardB = (int) Registry.GetValue(REG_NAME, REG_KEY3, cardB);
                tmpBool = (string) Registry.GetValue(REG_NAME, REG_KEY4, "False");
                if (tmpBool != null) shufflePiles = Convert.ToBoolean(tmpBool);
                tmpBool = (string) Registry.GetValue(REG_NAME, REG_KEY5, "False");
                if (tmpBool != null) showCardBack = Convert.ToBoolean(tmpBool);
            }
            catch (Exception ex) { /* ignore, go with defaults */ }

            if ((winX != -1) && (winY != -1)) this.SetDesktopLocation(winX, winY);
            if (Enum.IsDefined(typeof(CardBacks), cardB)) cardBack = (CardBacks) cardB;
            lblHighestScore.Text = Convert.ToString(stats.HighestScore);
        }

        private void SetupContextMenu()
        {
            ContextMenu mnu = new ContextMenu();
            MenuItem mnuStats = new MenuItem("Game Statistics");
            MenuItem sep = new MenuItem("-");
            MenuItem mnuAbout = new MenuItem("About");

            mnuStats.Click += new EventHandler(mnuStats_Click);
            mnuAbout.Click += new EventHandler(mnuAbout_Click);
            mnu.MenuItems.AddRange(new MenuItem[] { mnuStats, sep, mnuAbout });
            this.ContextMenu = mnu;
        }

        private void CreatePilesAndFoundations()
        {
            // typically, piles start with 6 cards, but need to have enough
            // room for cards to be stacked (added) into the piles
            for (int i = 0; i < NUM_PILES; i++)
                cardPiles[i] = new CardHand(MAX_IN_PILE, false);
            // foundations only need space for ace to king or king to ace
            for (int i = 0; i < NUM_FOUNDATIONS; i++) {
                downFoundations[i] = new CardHand(MAX_IN_FOUNDATION, false);
                upFoundations[i] = new CardHand(MAX_IN_FOUNDATION, false);
            }
        }

        private void InitDisplays()
        {
            pileDisplay[0]  = pbPile1;  pileDisplay[1]  = pbPile2;  pileDisplay[2]  = pbPile3;
            pileDisplay[3]  = pbPile4;  pileDisplay[4]  = pbPile5;  pileDisplay[5]  = pbPile6;
            pileDisplay[6]  = pbPile7;  pileDisplay[7]  = pbPile8;  pileDisplay[8]  = pbPile9;
            pileDisplay[9]  = pbPile10; pileDisplay[10] = pbPile11; pileDisplay[11] = pbPile12;
            pileDisplay[12] = pbPile13; pileDisplay[13] = pbPile14; pileDisplay[14] = pbPile15;
            pileDisplay[15] = pbPile16;
            dfDisplay[0] = pbFoundation1; dfDisplay[1] = pbFoundation2;
            dfDisplay[2] = pbFoundation3; dfDisplay[3] = pbFoundation4;
            ufDisplay[0] = pbFoundation5; ufDisplay[1] = pbFoundation6;
            ufDisplay[2] = pbFoundation7; ufDisplay[3] = pbFoundation8;
        }

        private void InitDragNDrop()
        {
            for (int i = 0; i < NUM_PILES; i++) {
                pileDisplay[i].MouseDown += pbPile_MouseDown;
                pileDisplay[i].AllowDrop = true;
                pileDisplay[i].DragEnter += pbPile_DragEnter;
                pileDisplay[i].DragDrop += pbPile_DragDrop;
                pileDisplay[i].QueryContinueDrag += pbX_QueryContinueDrag;
                pileDisplay[i].GiveFeedback += XX_GiveFeedback;
            }
            for (int i = 0; i < NUM_FOUNDATIONS; i++) {
                dfDisplay[i].MouseDown += pbDownFound_MouseDown;
                dfDisplay[i].AllowDrop = true;
                dfDisplay[i].DragEnter += pbDownFound_DragEnter;
                dfDisplay[i].DragDrop += pbDownFound_DragDrop;
                dfDisplay[i].QueryContinueDrag += pbX_QueryContinueDrag;
                dfDisplay[i].GiveFeedback += XX_GiveFeedback;
                ufDisplay[i].MouseDown += pbUpFound_MouseDown;
                ufDisplay[i].AllowDrop = true;
                ufDisplay[i].DragEnter += pbUpFound_DragEnter;
                ufDisplay[i].DragDrop += pbUpFound_DragDrop;
                ufDisplay[i].QueryContinueDrag += pbX_QueryContinueDrag;
                ufDisplay[i].GiveFeedback += XX_GiveFeedback;
            }
        }

        private void ShowScore()
        {
            if (curScore > highScore) {
                highScore = curScore;
                lblHighScore.Text = Convert.ToString(highScore);
                if (highScore > stats.HighestScore) {
                    lblHighestScore.Text = Convert.ToString(highScore);
                }
            }
            lblCurScore.Text = Convert.ToString(curScore);
        }

        private void ResetGame()
        {
            // clear piles/foundations
            for (int i = 0; i < NUM_PILES; i++)
                cardPiles[i].RemoveAll();
            for (int i = 0; i < NUM_FOUNDATIONS; i++) {
                downFoundations[i].RemoveAll();
                upFoundations[i].RemoveAll();
            }
            btnUndo.Enabled = false; undos.Clear();
            btnReshuffle.Enabled = true; shufflesDone = 0; lblShufLeft.Text = "3";
            curScore = 0;
            ShowScore();
        }

        private void DisplayCard(PictureBox display, CardHand foundOrPile)
        {
            if (foundOrPile.CurNumCardsInHand > 0)
                display.Image = images.GetCardImage(foundOrPile.CardAt(CardHand.FIRST));
            else
                display.Image = images.GetCardPlaceholderImage(placeholder);
        }

        private void DisplayPileCards()
        {
            for (int i = 0; i < NUM_PILES; i++) {
                DisplayCard(pileDisplay[i], cardPiles[i]);
            }
        }

        private void MoveAndReplace(CardHand foundOrPile, PlayingCard firstCard, int startIdx)
        {
            for (int i = startIdx; i >= 0; i--)
                foundOrPile.Replace(foundOrPile.CardAt(i), i + 1);
            foundOrPile.Replace(firstCard, 0);
        }

        private void MoveBottomPileCard(CardHand pile)
        {
            int cardsInPile = pile.CurNumCardsInHand;

            if (cardsInPile > 1) {
                PlayingCard bottomCard = pile.CardAt(cardsInPile - 1);
                MoveAndReplace(pile, bottomCard, cardsInPile - 2);
            }
        }

        private PlayingCard RemoveCardOnTop(CardHand foundOrPile)
        {
            PlayingCard ret = foundOrPile.GetFirstAvailableCard();
            foundOrPile.CompressHand();
            return ret;
        }

        private void PutCardOnTop(PlayingCard card, CardHand foundOrPile)
        {
            int cardsInHand = foundOrPile.CurNumCardsInHand;

            if (cardsInHand < foundOrPile.MaxCardsInHand) {
                // add a dummy card to back of foundation or pile (need slot to move to)
                foundOrPile.Add(PlayingCard.RED_JOKER);
                MoveAndReplace(foundOrPile, PlayingCard.EMPTY_CARD, cardsInHand - 1);
                foundOrPile.Replace(card, 0);
            }
        }

        private void ShowCardUnder(PictureBox foundOrPile, CardHand fndOrPile)
        {
            if (fndOrPile.CurNumCardsInHand > 1) {
                PlayingCard next = fndOrPile.CardAt(CardHand.NEXT);
                if (showCardBack)
                    foundOrPile.Image = images.GetCardBackImage(cardBack);
                else
                    foundOrPile.Image = images.GetCardImage(next);
            }
            else
                foundOrPile.Image = images.GetCardPlaceholderImage(placeholder);
        }

        private void DragCreateCursors()
        {
            Bitmap card = (Bitmap) images.GetCardImage(dragStartFoundOrPile.CardAt(CardHand.FIRST)).Clone();
            Bitmap nCard = (Bitmap) card.Clone();

            nCard.MakeTransparent(card.GetPixel(0, 0));
            cardMoveCursor = CursorUtil.CreateCursor(card, 35, 48);
            cardNoneCursor = CursorUtil.CreateCursor(nCard, 35, 48);
            useCustomCursors = ((cardMoveCursor != null) && (cardNoneCursor != null));
        }

        private void DragDisposeCursors()
        {
            useCustomCursors = false;
            if (cardMoveCursor != null) cardMoveCursor.Dispose();
            if (cardNoneCursor != null) cardNoneCursor.Dispose();
        }

        private bool DragCanPlaceCard(CardHand foundOrPile, bool isPile, bool foundDown)
        {
            bool ret = false;
            PlayingCard card = foundOrPile.CardAt(CardHand.FIRST);

            if (card != PlayingCard.EMPTY_CARD) {
                PlayingCard cardDragging = dragStartFoundOrPile.CardAt(CardHand.FIRST);

                if (card.Suit == cardDragging.Suit) {
                    int cardDrugged = (int) cardDragging.CardValue;
                    int cardBefore = (int) card.CardValue - 1;
                    int cardAfter = (int) card.CardValue + 1;

                    if ((cardBefore < 1) && (isPile)) cardBefore = 13;
                    if ((cardAfter > 13) && (isPile)) cardAfter = 1;

                    if ((isPile) && (!draggedFromFoundation) &&
                        ((cardDrugged == cardBefore) || (cardDrugged == cardAfter)))
                        ret = true;
                    else if (!isPile) {
                        if ((foundDown) && (cardDrugged == cardBefore))
                            ret = true;
                        else if ((!foundDown) && (cardDrugged == cardAfter))
                            ret = true;
                    }
                }
            }

            return ret;
        }

        private void DragMoved(PictureBox display, CardHand foundOrPile)
        {
            PutCardOnTop(dragStartFoundOrPile.CardAt(CardHand.FIRST), foundOrPile);
            DisplayCard(display, foundOrPile);
        }

        private bool CheckPileCardToFoundation(PlayingCard card)
        {
            bool ret = false;
            int found = (int) card.Suit - 1;
            int dcv = (int) downFoundations[found].CardAt(CardHand.FIRST).CardValue - 1;
            int ucv = (int) upFoundations[found].CardAt(CardHand.FIRST).CardValue + 1;

            if ((dcv == (int) card.CardValue) || (ucv == (int) card.CardValue)) 
                ret = true;

            return ret;
        }

        private bool CheckPileCards()
        {
            // check piles to piles and foundations
            for (int i = 0; i < (NUM_PILES - 1); i++) {
                if (cardPiles[i].CurNumCardsInHand > 0) {
                    PlayingCard card = cardPiles[i].CardAt(CardHand.FIRST);
                    int crd = (int)card.CardValue;
                    int cardBefore = crd - 1, cardAfter = crd + 1;
                    int pCardBefore = (cardBefore < 1 ? 13 : cardBefore);
                    int pCardAfter = (cardAfter > 13 ? 1 : cardAfter);

                    // check card to each card on top of the other piles
                    for (int j = (i + 1); j < NUM_PILES; j++) {
                        if (cardPiles[j].CurNumCardsInHand > 0) {
                            PlayingCard jcard = cardPiles[j].CardAt(CardHand.FIRST);
                            int cv = (int)jcard.CardValue;

                            if ((jcard.Suit == card.Suit) && ((cv == pCardBefore) || (cv == pCardAfter)))
                                return true;
                        }
                    }
                    // check card to foundations
                    if (CheckPileCardToFoundation(card)) return true;
                }
            }
            // check final pile card to foundation (above check misses this one)
            if (cardPiles[NUM_PILES - 1].CurNumCardsInHand > 0)
                return CheckPileCardToFoundation(cardPiles[NUM_PILES - 1].CardAt(CardHand.FIRST));

            return false;
        }

        private bool CheckFoundationCards()
        {
            // check foundations to foundations
            for (int i = 0; i < NUM_FOUNDATIONS; i++) {
                int dCard = (int) downFoundations[i].CardAt(CardHand.FIRST).CardValue;
                int uCard = (int) upFoundations[i].CardAt(CardHand.FIRST).CardValue;
                int fdcv = dCard - 1, fucv = uCard + 1;

                if ((fdcv == uCard) || (fucv == dCard)) return true;
            }

            return false;
        }

        private bool CheckWon()
        {
            for (int i = 0; i < NUM_PILES; i++) {
                if (cardPiles[i].CurNumCardsInHand > 0) return false;
            }

            return true;
        }

        private void CheckTheEnd()
        {
            if (CheckWon()) {
                if (shufflesDone < MAX_SHUFFLES) {
                    // add on bonus points
                    curScore += (MAX_SHUFFLES - shufflesDone) * SHUFFLE_BONUS;
                    ShowScore();
                }
                stats.GameWon(curScore);
                btnReshuffle.Enabled = false; btnUndo.Enabled = false; undos.Clear();
                MessageBox.Show("Congratulations, you've won the game.", this.Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
            else if ((!CheckPileCards()) && (!CheckFoundationCards())) {
                // no moves, check if can shuffle else end game
                if (shufflesDone < MAX_SHUFFLES)
                    MessageBox.Show("No moves left, need to shuffle piles.", this.Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                else {
                    stats.GameLost(curScore);
                    MessageBox.Show("No moves left, game over.", this.Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Hand);
                }
            }
        }

        private void DragMoveComplete()
        {
            cardImageDragging = false;
            RemoveCardOnTop(dragStartFoundOrPile);
            DisplayCard(dragStartPB, dragStartFoundOrPile);
            CheckTheEnd();
        }

        private void StartUndo(UndoTargets from, int idx)
        {
            scratch = new Undo();
            scratch.from = from;
            scratch.fromIdx = idx;
        }

        private void PushUndo(UndoTargets to, int idx)
        {
            scratch.to = to;
            scratch.toIdx = idx;
            undos.Push(scratch);
            btnUndo.Enabled = true;
        }

        private void DoUndo(CardHand fromFP, CardHand toFP, PictureBox fromPB, PictureBox toPB, bool minusScore)
        {
            PlayingCard top = RemoveCardOnTop(toFP);

            PutCardOnTop(top, fromFP);
            DisplayCard(fromPB, fromFP);
            DisplayCard(toPB, toFP);

            if (minusScore) {
                curScore--;
                // if undid king or ace from foundation, take away bonus points also
                if ((top.CardValue == CardValue.Ace) || (top.CardValue == CardValue.King))
                    curScore = curScore - FOUND_FINISH_BONUS;
                ShowScore();
            }

            btnUndo.Enabled = (undos.Count > 0);
        }

        #endregion

        // --------------------------------------------------------------------

        public MainWin()
        {
            InitializeComponent();
        }

        // --------------------------------------------------------------------

        #region Event Handlers

        private void MainWin_Load(object sender, EventArgs e)
        {
            LoadFromRegistry();
            SetupContextMenu();
            CreatePilesAndFoundations();
            InitDisplays();
            InitDragNDrop();
            stats.GameName = this.Text;
        }

        private void MainWin_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (this.WindowState == FormWindowState.Normal) {
                Registry.SetValue(REG_NAME, REG_KEY1, this.Location.X);
                Registry.SetValue(REG_NAME, REG_KEY2, this.Location.Y);
            }
        }

        private void btnNew_Click(object sender, EventArgs e)
        {
            ResetGame();
            cards.Shuffle();

            // deal out the cards
            int pile = 0;
            while (cards.HasMoreCards()) {
                PlayingCard card = cards.GetNextCard();
                int fnd = (int) card.Suit - 1;

                if ((card.CardValue == CardValue.King) &&
                    (downFoundations[fnd].CurNumCardsInHand == 0)) {
                    downFoundations[fnd].Add(card);
                    dfDisplay[fnd].Image = images.GetCardImage(card);
                }
                else if ((card.CardValue == CardValue.Ace) &&
                         (upFoundations[fnd].CurNumCardsInHand == 0)) {
                    upFoundations[fnd].Add(card);
                    ufDisplay[fnd].Image = images.GetCardImage(card);
                }
                else {
                    cardPiles[pile].Add(card); pile++;
                    if (pile == NUM_PILES) pile = 0;
                }
            }
            DisplayPileCards();
            stats.StartGame(true);
        }

        private void btnOptions_Click(object sender, EventArgs e)
        {
            OptionsWin opts = new OptionsWin();

            opts.Images = images;
            opts.CardBack = cardBack;
            opts.ShufflePiles = shufflePiles;
            opts.ShowCardBack = showCardBack;

            if (opts.ShowDialog(this) == DialogResult.OK) {
                cardBack = opts.CardBack;
                shufflePiles = opts.ShufflePiles;
                showCardBack = opts.ShowCardBack;
                Registry.SetValue(REG_NAME, REG_KEY3, (int) cardBack);
                Registry.SetValue(REG_NAME, REG_KEY4, shufflePiles);
                Registry.SetValue(REG_NAME, REG_KEY5, showCardBack);
            }

            opts.Dispose();
        }

        private void btnHelp_Click(object sender, EventArgs e)
        {
            var asm = Assembly.GetEntryAssembly();
            var asmLocation = Path.GetDirectoryName(asm.Location);
            var htmlPath = Path.Combine(asmLocation, HTML_HELP_FILE);

            try {
                Process.Start(htmlPath);
            }
            catch (Exception ex) {
                MessageBox.Show("Cannot load help: " + ex.Message, "Help Load Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnExit_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void btnReshuffle_Click(object sender, EventArgs e)
        {
            shufflesDone++;

            for (int i = 0; i < NUM_PILES; i++) {
                if (shufflePiles)
                    cardPiles[i].Shuffle();
                else
                    MoveBottomPileCard(cardPiles[i]);
            }
            DisplayPileCards();

            lblShufLeft.Text = Convert.ToString(MAX_SHUFFLES - shufflesDone);
            if (shufflesDone == MAX_SHUFFLES) btnReshuffle.Enabled = false;
            btnUndo.Enabled = false; undos.Clear();

            CheckTheEnd();
        }

        private void btnUndo_Click(object sender, EventArgs e)
        {
            Undo top = undos.Pop();
            CardHand fromFP = null, toFP = null;
            PictureBox fromPB = null, toPB = null;
            bool minusScore = ((top.from == UndoTargets.Pile) && (top.to != UndoTargets.Pile));

            switch (top.from) {
                case UndoTargets.Pile :
                    fromFP = cardPiles[top.fromIdx];
                    fromPB = pileDisplay[top.fromIdx];
                    break;
                case UndoTargets.DownFoundation :
                    fromFP = downFoundations[top.fromIdx];
                    fromPB = dfDisplay[top.fromIdx];
                    break;
                case UndoTargets.UpFoundation :
                    fromFP = upFoundations[top.fromIdx];
                    fromPB = ufDisplay[top.fromIdx];
                    break;
            }
            switch (top.to) {
                case UndoTargets.Pile:
                    toFP = cardPiles[top.toIdx];
                    toPB = pileDisplay[top.toIdx];
                    break;
                case UndoTargets.DownFoundation:
                    toFP = downFoundations[top.toIdx];
                    toPB = dfDisplay[top.toIdx];
                    break;
                case UndoTargets.UpFoundation:
                    toFP = upFoundations[top.toIdx];
                    toPB = ufDisplay[top.toIdx];
                    break;
            }
            DoUndo(fromFP, toFP, fromPB, toPB, minusScore);
        }

        private void mnuStats_Click(object sender, EventArgs e)
        {
            stats.ShowStatistics(this);
        }

        private void mnuAbout_Click(object sender, EventArgs e)
        {
            AboutBox about = new AboutBox();

            about.ShowDialog(this);
            about.Dispose();
        }

        #endregion

        // --------------------------------------------------------------------

        #region Drag-n-Drop Support Handlers

        private void XX_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            if (useCustomCursors) {
                // Sets the custom cursor based upon the effect.
                e.UseDefaultCursors = false;
                if ((e.Effect & DragDropEffects.Move) == DragDropEffects.Move)
                    Cursor.Current = cardMoveCursor;
                else
                    Cursor.Current = cardNoneCursor;
                if (dragStarting) {
                    // 'peek' at card under card dragging (if set)
                    dragStarting = false;
                    ShowCardUnder(dragStartPB, dragStartFoundOrPile);
                }
            }
            else
                e.UseDefaultCursors = true;
        }

        private void pbX_MouseDown(object sender, MouseEventArgs e, CardHand foundOrPile)
        {
            cardImageDragging = true; dragStarting = true;
            dragStartPB = sender as PictureBox;
            dragStartFoundOrPile = foundOrPile;
            DragCreateCursors();
            if (DoDragDrop(dragStartPB.Image, DragDropEffects.Move) == DragDropEffects.Move)
                DragMoveComplete();
            else {
                cardImageDragging = false; dragStarting = false;
                DragDisposeCursors();
                DisplayCard(dragStartPB, dragStartFoundOrPile);
            }

        }

        private void pbPile_MouseDown(object sender, MouseEventArgs e)
        {
            int pile = Convert.ToInt32((sender as PictureBox).Tag);

            StartUndo(UndoTargets.Pile, pile);
            if (cardPiles[pile].CurNumCardsInHand > 0) {
                draggedFromFoundation = false;
                pbX_MouseDown(sender, e, cardPiles[pile]);
            }
        }

        private void pbXFound_MouseDown(object sender, MouseEventArgs e, CardHand[] founds, UndoTargets which)
        {
            int foundation = Convert.ToInt32((sender as PictureBox).Tag);

            StartUndo(which, foundation);
            if (founds[foundation].CurNumCardsInHand > 1) { // initial card can't be moved...
                draggedFromFoundation = true;
                pbX_MouseDown(sender, e, founds[foundation]);
            }
        }

        private void pbDownFound_MouseDown(object sender, MouseEventArgs e)
        {
            pbXFound_MouseDown(sender, e, downFoundations, UndoTargets.DownFoundation);
        }

        private void pbUpFound_MouseDown(object sender, MouseEventArgs e)
        {
            pbXFound_MouseDown(sender, e, upFoundations, UndoTargets.UpFoundation);
        }

        private void pbPile_DragEnter(object sender, DragEventArgs e)
        {
            if ((cardImageDragging) &&
                (e.Data.GetDataPresent(DataFormats.Bitmap)) &&
                (sender != dragStartPB)) {
                int pile = Convert.ToInt32((sender as PictureBox).Tag);
                if (DragCanPlaceCard(cardPiles[pile], true, true))
                    e.Effect = DragDropEffects.Move;
                else
                    e.Effect = DragDropEffects.None;
            }
        }

        private void pbDownFound_DragEnter(object sender, DragEventArgs e)
        {
            if ((cardImageDragging) &&
                (e.Data.GetDataPresent(DataFormats.Bitmap)) &&
                (sender != dragStartPB)) {
                int foundation = Convert.ToInt32((sender as PictureBox).Tag);
                if (DragCanPlaceCard(downFoundations[foundation], false, true))
                    e.Effect = DragDropEffects.Move;
                else
                    e.Effect = DragDropEffects.None;
            }
        }

        private void pbUpFound_DragEnter(object sender, DragEventArgs e)
        {
            if ((cardImageDragging) &&
                (e.Data.GetDataPresent(DataFormats.Bitmap)) &&
                (sender != dragStartPB)) {
                int foundation = Convert.ToInt32((sender as PictureBox).Tag);
                if (DragCanPlaceCard(upFoundations[foundation], false, false))
                    e.Effect = DragDropEffects.Move;
                else
                    e.Effect = DragDropEffects.None;
            }
        }

        private int pbX_DragDrop(object sender, DragEventArgs e, CardHand[] foundsOrPiles, UndoTargets which)
        {
            int idx = Convert.ToInt32((sender as PictureBox).Tag);
            DragMoved(sender as PictureBox, foundsOrPiles[idx]);
            PushUndo(which, idx);
            return idx;
        }

        private void pbPile_DragDrop(object sender, DragEventArgs e)
        {
            // var bmp = (Bitmap) e.Data.GetData(DataFormats.Bitmap);
            // ((PictureBox) sender).Image = bmp;
            pbX_DragDrop(sender, e, cardPiles, UndoTargets.Pile);
        }

        private void pbXFound_Score(CardHand foundation, bool down)
        {
            if (!draggedFromFoundation) curScore++;
            CardValue cv = foundation.CardAt(CardHand.FIRST).CardValue;
            if ((down) && (cv == CardValue.Ace))
                curScore = curScore + FOUND_FINISH_BONUS;
            else if ((!down) && (cv == CardValue.King))
                curScore = curScore + FOUND_FINISH_BONUS;
            ShowScore();
        }

        private void pbDownFound_DragDrop(object sender, DragEventArgs e)
        {
            int idx = pbX_DragDrop(sender, e, downFoundations, UndoTargets.DownFoundation);
            pbXFound_Score(downFoundations[idx], true);
        }

        private void pbUpFound_DragDrop(object sender, DragEventArgs e)
        {
            int idx = pbX_DragDrop(sender, e, upFoundations, UndoTargets.UpFoundation);
            pbXFound_Score(upFoundations[idx], false);
        }

        private void pbX_QueryContinueDrag(object sender, QueryContinueDragEventArgs e)
        {
            // Cancel the drag if the mouse moves off the form.
            if ((Control.MousePosition.X < this.DesktopBounds.Left) ||
                (Control.MousePosition.X > this.DesktopBounds.Right) ||
                (Control.MousePosition.Y < this.DesktopBounds.Top) ||
                (Control.MousePosition.Y > this.DesktopBounds.Bottom)) {
                e.Action = DragAction.Cancel;
            }
        }

        #endregion
    }
}
