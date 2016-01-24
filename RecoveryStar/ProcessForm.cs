/*----------------------------------------------------------------------+
 |	filename:	ProcessForm.cs                                          |
 |----------------------------------------------------------------------|
 |	version:	1.14                                                    |
 |	revision:   02/01/2007	08:57                                       |
 |  author:		Дробанов Артём Федорович (DrAF)                         |
 |	e-mail:		draf@mail.ru                                            |
 |	purpose:	Работа с файлами                                        |
 +----------------------------------------------------------------------+
 |  Отказоустойчивое кодирование по типу RAID систем                    |
 +----------------------------------------------------------------------*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.IO;

namespace RecoveryStar
{    
    /// <summary>
    /// Делегат обновления строкового значения
    /// </summary>
    /// <param name="text">Строковое значение</param>
    public delegate void OnUpdateStringValueHandler(String text);
    
    /// <summary>
    /// Делегат обновления числового значения
    /// </summary>        
    /// <param name="value">Числовое значение</param>
    public delegate void OnUpdateDoubleValueHandler(double value);

    /// <summary>
    /// Делегат вывода строкового значения вместе с числовым
    /// </summary>
    /// <param name="text">Строковое значение</param>
    /// <param name="value">Числовое значение</param>
    public delegate void OnUpdateStringAndDoubleValueHandler(String text, double value);

    /// <summary>
    /// Делегат без параметров
    /// </summary>
    public delegate void OnUpdateControlsHandler();

    /// <summary>
    /// Режимы работы приложения (Защита, Восстановление, Лечение, Тестирование)
    /// </summary>
    public enum RSModes {None, Protect, Recover, Repair, Test};

    public partial class ProcessForm : Form
    {
        #region Construction & Destruction

        public ProcessForm()
        {
            InitializeComponent();

            // Инициализируем экземпляр класса для упаковки (распаковки) имени файла
            // в префиксный формат
            _eFileNamer = new FileNamer();

            // Создаем экземпляр класса ядра RecoveryStar
            _eRecoveryStarCore = new RecoveryStarCore();
         
            // Подписываемся на требуемых делегатов
            _eRecoveryStarCore.OnUpdateFileSplittingProgress = new OnUpdateFileSplittingProgressHandler(OnUpdateFileSplittingProgress);
            _eRecoveryStarCore.OnFileSplittingFinish = new OnFileSplittingFinishHandler(OnFileSplittingFinish);

            _eRecoveryStarCore.OnUpdateRSMatrixFormingProgress = new OnUpdateRSMatrixFormingProgressHandler(OnUpdateRSMatrixFormingProgress);
            _eRecoveryStarCore.OnRSMatrixFormingFinish = new OnRSMatrixFormingFinishHandler(OnRSMatrixFormingFinish);
            _eRecoveryStarCore.OnUpdateFileCodingProgress = new OnUpdateFileCodingProgressHandler(OnUpdateFileCodingProgress);
            _eRecoveryStarCore.OnFileCodingFinish = new OnFileCodingFinishHandler(OnFileCodingFinish);

            _eRecoveryStarCore.OnUpdateFileAnalyzeProgress = new OnUpdateFileAnalyzeProgressHandler(OnUpdateFileAnalyzeProgress);
            _eRecoveryStarCore.OnFileAnalyzeFinish = new OnFileAnalyzeFinishHandler(OnFileAnalyzeFinish);
            _eRecoveryStarCore.OnGetDamageStat = new OnGetDamageStatHandler(OnGetDamageStat);

            // Инициализируем список файлов для обработки
            _filenamesToProcess = new ArrayList();

            // Считываем значение с элемента управления, ответственного за
            // приоритет процесса обработки данных
            SetThreadPriority(processPriorityComboBox.SelectedIndex);
            
            // Инициализируем событие прекращения обработки файла
            _exitEvent = new ManualResetEvent[] { new ManualResetEvent(false) };

            // Инициализируем cобытие продолжения обработки файла
            _executeEvent = new ManualResetEvent[] { new ManualResetEvent(false) };

            // Инициализируем cобытие "пробуждения" цикла ожидания
            _wakeUpEvent = new ManualResetEvent[] { new ManualResetEvent(false) };
            
            // Устанавливаем значение по-умолчанию для приоритета...
            processPriorityComboBox.Text = "По-умолчанию";
            //...и переносим фокус на кнопку, активную по-умолчанию
            pauseButton.Focus();
        }

        #endregion Construction & Destruction
    
        #region Public Operations

        /// <summary>
        /// Метод остановки потока
        /// </summary>
        public void Stop()
        {
            // Указываем, что поток обработки больше не должен выполняться            
            _executeEvent[0].Set(); // Сначала снимаем режим "Пауза"...
            _wakeUpEvent[0].Set();  //...а, затем, снимаем с "дремоты"...     
            _exitEvent[0].Set();    //...и указываем, что требуется выход
        }

        /// <summary>
        /// Постановка потока обработки на паузу
        /// </summary>
        public void Pause()
        {
            // Ставим обработку на паузу
            _executeEvent[0].Reset(); // Сначала устанавливаем режим "Пауза"...
            _wakeUpEvent[0].Set();    //...а, затем, снимаем с "дремоты"...            
        }

        /// <summary>
        /// Снятие потока обработки с паузы
        /// </summary>
        public void Continue()
        {
            // Снимаем обработку c паузы            
            _executeEvent[0].Set();                        
        }
        
        #endregion Public Operations

        #region Private Operations

        /// <summary>
        /// Поток обработки данных
        /// </summary>
        private void Process()
        {           
            // Номер обрабатываемого файла
            int fileNum = 0;

            // Сбрасываем статистику обработки
            _OKCount = 0;
            _errorCount = 0;

            // Строка, хранящая значение общего количества обрабатываемых файлов
            String filesTotal = Convert.ToString(_filenamesToProcess.Count);

            // Строка, хранящая текст для вывода на экран
            String textToOut = "";
            
            // Обрабатываем все файлы из представленного списка
            foreach (String fullFilename in _filenamesToProcess)
            {                
                // Получаем короткий вариант длинного имени
                String shortFilename = _eFileNamer.GetShortFilename(fullFilename);

                // Имя файла для вывода на экран
                String unpackedFilename = shortFilename;

                // Если используется режим не защиты данных,
                // требуется распаковка имени из префиксного формата
                if (_mode != RSModes.Protect)
                {
                    // Распаковываем короткий вариант имени с получением оригинального
                    unpackedFilename = shortFilename;

                    // Если не удалось корректно распаковать короткое имя - переходим
                    // на следующую итерацию
                    if (!_eFileNamer.Unpack(ref unpackedFilename))
                    {
                        continue;
                    }
                }

                // Подготавливаем текст для вывода в заголовок формы
                if (_mode == RSModes.Protect) { textToOut = "Защита файла \""; }
                if (_mode == RSModes.Recover) { textToOut = "Извлечение файла \""; }
                if (_mode == RSModes.Repair)  { textToOut = "Лечение томов файла \""; }
                if (_mode == RSModes.Test)    { textToOut = "Тестирование файла \""; }

                textToOut += unpackedFilename + "\" (" + Convert.ToString(++fileNum) + " из " + filesTotal + ")";                
                // Выводим текст в заголовок формы
                if (this.InvokeRequired) this.Invoke(((OnUpdateStringValueHandler)delegate(String value) { this.Text = value; }), new object[] { textToOut });                               

                // Если запущен режим защиты данных
                if (_mode == RSModes.Protect)
                {
                    // Отключаем те элементы управления, которые не будут
                    // использоваться в контексте текущего процесса
                    if (fileAnalyzeStatGroupBox.InvokeRequired) this.Invoke(((OnUpdateControlsHandler)delegate() { fileAnalyzeStatGroupBox.Enabled = false; }), new object[] { });
                    if (percOfDamageLabel.InvokeRequired) this.Invoke(((OnUpdateControlsHandler)delegate() { percOfDamageLabel.Enabled = false; }), new object[] { });
                    if (percOfAltEccLabel.InvokeRequired) this.Invoke(((OnUpdateControlsHandler)delegate() { percOfAltEccLabel.Enabled = false; }), new object[] { });                    

                    // Запускаем отказоустойчивое кодирование
                    _eRecoveryStarCore.StartToProtect(fullFilename, _dataCount, _eccCount, true);
                }

                // Если запущен режим для восстановления
                if (_mode == RSModes.Recover)
                {
                    // Запускаем восстановление данных
                    _eRecoveryStarCore.StartToRecover(fullFilename, true);
                }

                // Если запущен режим лечения
                if (_mode == RSModes.Repair)
                {
                    // Запускаем восстановление данных
                    _eRecoveryStarCore.StartToRepair(fullFilename, true);
                }

                // Если запущен режим тестирования
                if (_mode == RSModes.Test)
                {
                    // Запускаем восстановление данных
                    _eRecoveryStarCore.StartToTest(fullFilename, true);
                }

                // Ждем окончания обработки
                while (true)
                {
                    // Если не обнаружили установленного события "_executeEvent",
                    // то пользователь хочет, чтобы мы поставили обработку на паузу -                       
                    if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                    {
                        //...приостанавливаем работу контролируемого алгоритма...                      
                        _eRecoveryStarCore.Pause();
                      
                        //...и сами засыпаем
                        ManualResetEvent.WaitAll(_executeEvent);

                        // А когда проснулись, указываем, что обработка должна продолжаться
                        _eRecoveryStarCore.Continue();
                    } 

                    // Ждем любое из перечисленных событий...
                    int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eRecoveryStarCore.FinishedEvent[0] });

                    //...если получили сигнал к тому, чтобы проснуться -
                    // переходим на новую итерацию, т.к. просыпаемся
                    // перед постановкой на паузу...
                    if (eventIdx == 0)
                    {
                        //...предварительно сбросив событие, заставившее нас проснуться
                        _wakeUpEvent[0].Reset();

                        continue;
                    }

                    //...если получили сигнал к выходу из обработки...
                    if (eventIdx == 1)
                    {
                        ///...останавливаем контролируемый алгоритм
                        _eRecoveryStarCore.Stop();
                      
                        return;
                    }

                    //...если получили сигнал о завершении обработки вложенным алгоритмом...
                    if (eventIdx == 2)
                    {
                        //...выходим из цикла ожидания завершения (этого и ждали в while(true)!)
                        break;
                    }

                } // while(true)

                // В связи с закрытием большого количества файловых потоков
                // необходимо дождаться записи изменений, внесенных потоком
                // кодирования в тело класса. Поток уже не работает, но
                // установленное им булевское свойство, возможно, ещё
                // "не проявилось". Достаточно необычная ситуация,
                // но факт - вещь упрямая.
                for (int i = 0; i < 9000; i++)
                {
                    if (!_eRecoveryStarCore.Finished)
                    {
                        Thread.Sleep(100);
                    }
                }

                // Производим проверку на корректность обработки
                if (_eRecoveryStarCore.ProcessedOK)
                {
                    // Если обработка файла произошла корректно...                    
                    OnUpdateLogListBox(this.Text + " : OK!");

                    // Изменяем статистику
                    _OKCount++;

                    // Формируем текст для вывода на форму
                    textToOut = Convert.ToString(_OKCount);
                    if (OKCountLabel.InvokeRequired) this.Invoke(((OnUpdateStringValueHandler)delegate(String value) { OKCountLabel.Text = value; }), new object[] { textToOut });                   

                } else
                {
                    // Если обработка файла произошла некорректно...                    
                    OnUpdateLogListBox(this.Text + " : ERROR!");

                    // Изменяем статистику
                    _errorCount++;

                    // Формируем текст для вывода на форму
                    textToOut = Convert.ToString(_errorCount);
                    if (errorCountLabel.InvokeRequired) this.Invoke(((OnUpdateStringValueHandler)delegate(String value) { errorCountLabel.Text = value; }), new object[] { textToOut });
                }

                // Создаем пробел для отделения блоков строк друг от друга
                OnUpdateLogListBox("");
            }

            // Если используется режим восстановления данных, то
            // после декодирования все ненужные файлы удаляются
            if (_mode == RSModes.Recover)
            {
                try
                {
                    foreach (String fullFilename in _filenamesToProcess)
                    {                                                                      
                        // Производим выделение пути из полного имени файла
                        String path = _eFileNamer.GetPath(fullFilename);

                        // Производим выделение имени из полного имени файла
                        String filename = _eFileNamer.GetShortFilename(fullFilename);
                        
                        // Если имя корректно распаковывается - оно подлежит удалению,
                        // т.к. является уже не нужным фрагментом многотомной структуры
                        if (!_eFileNamer.Unpack(ref filename, ref _dataCount, ref _eccCount))
                        {
                            continue;
                        }
                        
                        // Обрабатываем все файлы
                        for (int i = 0; i < (_dataCount + _eccCount); i++)
                        {
                            // Считываем первоначальное имя файла,...
                            String volumename = filename;

                            //...упаковываем его в префиксный формат...
                            _eFileNamer.Pack(ref volumename, i, _dataCount, _eccCount);

                            //...формируем полное имя файла...
                            volumename = path + volumename;

                            //...производим тест на наличие файла...                    
                            if (File.Exists(volumename))
                            {
                                //...если таковой имеется, ставим на него атрибуты
                                // по-умолчанию...
                                File.SetAttributes(volumename, FileAttributes.Normal);
                                //...и затем удаляем
                                File.Delete(volumename);
                            }
                        }
                    }
                }

                catch (IOException e) { }
            }
            
            // Формируем текст для вывода...
            textToOut = "Закрыть";
            //...изменяем надпись на кнопке прекращения обработки...
            if (stopButton.InvokeRequired) this.Invoke(((OnUpdateStringValueHandler)delegate(String value) { stopButton.Text = value; }), new object[] { textToOut });
            
            //...и отключаем кнопку "Пауза"...
            if (pauseButton.InvokeRequired) this.Invoke(((OnUpdateControlsHandler)delegate() { pauseButton.Enabled = false; }), new object[] { });

            //...и выпадающий список выбора приоритета процесса
            if (processPriorityComboBox.InvokeRequired) this.Invoke(((OnUpdateControlsHandler)delegate() { processPriorityComboBox.Enabled = false; }), new object[] { });            
        }
       
        /// <summary>
        /// Метод установки приоритета процесса обработки на основании переданного значения int
        /// </summary>
        /// <param name="value"></param>
        private void SetThreadPriority(int value)
        {
            if (
                    (_thrRecoveryStarProcess != null)
                 &&
                    (_thrRecoveryStarProcess.IsAlive)
                )
            {
                switch (value)
                {
                    default:
                    case 0:
                    {
                        _threadPriority = System.Threading.ThreadPriority.Lowest;

                        break;
                    }

                    case 1:
                    {
                        _threadPriority = System.Threading.ThreadPriority.BelowNormal;

                        break;
                    }

                    case 2:
                    {
                        _threadPriority = System.Threading.ThreadPriority.Normal;

                        break;
                    }

                    case 3:
                    {
                        _threadPriority = System.Threading.ThreadPriority.AboveNormal;

                        break;
                    }

                    case 4:
                    {
                        _threadPriority = System.Threading.ThreadPriority.Highest;

                        break;
                    }
                }

                // Устанавливаем выбранный приоритет
                _thrRecoveryStarProcess.Priority = _threadPriority;             
                _eRecoveryStarCore.ThreadPriority = value;
            }
        }

        /// <summary>
        /// Вывод строкового значения в заголовок элемента управления "processGroupBox"
        /// </summary>
        /// <param name="text">Текст, описывающий процентное значение</param>
        /// <param name="progress">Процентное значение прогресса</param>
        private void OnUpdateProgressGroupBox(String text, double progress)
        {
            if (processGroupBox.InvokeRequired) processGroupBox.Invoke(new OnUpdateStringAndDoubleValueHandler(OnUpdateProgressGroupBox), new object[] { text, progress });            
            else
            {
                processGroupBox.Text = " " + text + ": " + Convert.ToString((int)(progress) + " %");
            }
        }

        /// <summary>
        /// Вывод строкового значения, завершающего обработку
        /// в заголовок элемента управления "processGroupBox"
        /// </summary>
        /// <param name="text">Текст для вывода</param>    
        private void OnFinishProgressGroupBox(String text)
        {
            if (processGroupBox.InvokeRequired) processGroupBox.Invoke(new OnUpdateStringValueHandler(OnFinishProgressGroupBox), new object[] { text });
            else
            {
                processGroupBox.Text = " " + text + ": завершено";

                // Выводим 100% в "processProgressBar"
                OnUpdateProcessProgressBar(100.0);
            }
        }

        /// <summary>
        /// Вывод значения прогресса в элемент управления "processProgressBar"
        /// </summary>        
        /// <param name="progress">Процентное значение прогресса</param>
        private void OnUpdateProcessProgressBar(double progress)
        {
            if (processProgressBar.InvokeRequired) processProgressBar.Invoke(new OnUpdateDoubleValueHandler(OnUpdateProcessProgressBar), new object[] { progress });
            else
            {
                processProgressBar.Value = (int)progress;
            }
        }

        /// <summary>
        /// Обработчик события "Обновление прогресса обработки томов"
        /// </summary>
        /// <param name="progress">Значение прогресса в процентах</param>
        private void OnUpdateFileSplittingProgress(double progress)
        {
            OnUpdateProgressGroupBox("Обработка томов", progress);
            OnUpdateProcessProgressBar(progress);
        }

        /// <summary>
        /// Обработчик события "Завершение процесса обработки томов"
        /// </summary>        
        private void OnFileSplittingFinish()
        {            
            OnFinishProgressGroupBox("Обработка томов");
        }

        /// <summary>
        /// Обработчик события "Обновление прогресса расчета матрицы кодирования Рида-Соломона"
        /// </summary>      
        private void OnUpdateRSMatrixFormingProgress(double progress)
        {            
            OnUpdateProgressGroupBox("Расчет матрицы кодирования", progress);
            OnUpdateProcessProgressBar(progress);
        }

        /// <summary>
        /// Обработчик события "Завершение расчета матрицы кодирования Рида-Соломона"
        /// </summary>        
        private void OnRSMatrixFormingFinish()
        {            
            OnFinishProgressGroupBox("Расчет матрицы кодирования");
        }

        /// <summary>
        /// Обработчик события "Обновление прогресса процесса кодирования томов"
        /// </summary>        
        private void OnUpdateFileCodingProgress(double progress)
        {            
            OnUpdateProgressGroupBox("Кодирование Рида-Соломона", progress);
            OnUpdateProcessProgressBar(progress);
        }

        /// <summary>
        /// Обработчик события "Завершение процесса кодирования томов"
        /// </summary>        
        private void OnFileCodingFinish()
        {            
            OnFinishProgressGroupBox("Кодирование Рида-Соломона");
        }

        /// <summary>
        /// Обработчик события "Обновление прогресса процесса анализа томов"
        /// </summary>        
        private void OnUpdateFileAnalyzeProgress(double progress)
        {            
            OnUpdateProgressGroupBox("Контроль целостности данных", progress);
            OnUpdateProcessProgressBar(progress);
        }

        /// <summary>
        /// Обработчик события "Завершение процесса анализа томов"
        /// </summary>        
        private void OnFileAnalyzeFinish()
        {
            OnFinishProgressGroupBox("Контроль целостности данных");
        }

        /// <summary>
        /// Обработчик события "Получение статистики повреждений томов"
        /// </summary>        
        private void OnGetDamageStat(double percOfDamage, double percOfAltEcc)
        {
            if (this.InvokeRequired) this.Invoke(new OnGetDamageStatHandler(OnGetDamageStat), new object[] { percOfDamage, percOfAltEcc });
            else
            {
                // Сначала сбрасываем результаты предыдущих выводов на экран
                percOfDamageLabel.Text = "Всего поврежденных томов: ";
                percOfAltEccLabel.Text = "Резерв томов для восстановления: ";
                                                
                // Анализируем данные, переданные ядром обработки
                if (
                        (percOfDamage == -1)
                     &&
                        (percOfAltEcc == -1)
                    )
                {
                    // Если все основные тома повреждены - выводим статистику в лог...                    
                    logListBox.Items.Add(this.Text + " : Все основные тома повреждены!");

                    //...и выходим
                    return;
                }
                
                if (
                        (percOfDamage == 0)
                     &&
                        (percOfAltEcc == -1)
                    )
                {
                    // Если нет повреждений в области основных томов - выводим статистику в лог...                    
                    logListBox.Items.Add(this.Text + " : Нет повреждений основных томов!");
                    
                    //...и выходим
                    return;
                }

                // Выводим статистику повреждений                
                percOfDamageLabel.Text = "Всего поврежденных томов: " + Convert.ToString((int)(percOfDamage)) + " %";
                percOfAltEccLabel.Text = "Резерв томов для восстановления: " + Convert.ToString((int)(percOfAltEcc)) + " %";
                logListBox.Items.Add("Всего поврежденных томов: " + Convert.ToString((int)(percOfDamage)) + " %");
                logListBox.Items.Add("Резерв томов для восстановления: " + Convert.ToString((int)(percOfAltEcc)) + " %");                
            }
        }

        /// <summary>
        /// Обработчик события "Обновление лога процесса обработки"
        /// </summary>        
        private void OnUpdateLogListBox(String text)
        {
            if (logListBox.InvokeRequired) logListBox.Invoke(new OnUpdateStringValueHandler(OnUpdateLogListBox), new object[] { text });
            else
            {
                logListBox.Items.Add(text);
            }
        }
      
        private void stopButton_Click(object sender, EventArgs e)
        {
            // Прекращение обработки данных...
            Stop();
            Close();
        }

        private void ProcessForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Прекращение обработки данных...
            Stop();
        }

        private void ProcessForm_Load(object sender, EventArgs e)
        {      
            // Указываем, что поток должен выполняться
            _exitEvent[0].Reset();
            _executeEvent[0].Set();
          
            // Создаем поток обработки данных...
            _thrRecoveryStarProcess = new Thread(new ThreadStart(Process));

            //...затем даем ему имя...
            _thrRecoveryStarProcess.Name = "RecoveryStar.Process()";

            //...устанавливаем выбранный приоритет задачи...
            _thrRecoveryStarProcess.Priority = _threadPriority;

            //...и запускаем его
            _thrRecoveryStarProcess.Start();
        }

        private void pauseButton_Click(object sender, EventArgs e)
        {
            if (pauseButton.Text == "Пауза")
            {
                pauseButton.Image = global::RecoveryStar.Properties.Resources.Start;
                pauseButton.Text = "Продолжить";

                // Ставим обработку на паузу
                Pause();

            } else
            {
                pauseButton.Image = global::RecoveryStar.Properties.Resources.Pause;
                pauseButton.Text = "Пауза";

                // Снимаем обработку с паузы
                Continue();                
            }
        }

        private void processPriorityTrackBar_ValueChanged(object sender, EventArgs e)
        {            
            // Считываем значение с элемента управления, ответственного за
            // приоритет процесса обработки данных
            SetThreadPriority(processPriorityComboBox.SelectedIndex);
        }

        private void processPriorityComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            pauseButton.Focus();            
        }

        #endregion Private Operations

        #region Public Properties
       
        /// <summary>
        /// Список полных имен файлов для обработки
        /// </summary>
        public ArrayList FilenamesToProcess
        {
            get
            {
                return _filenamesToProcess;
            }

            set
            {
                _filenamesToProcess = value;
            }
        }
      
        /// <summary>
        /// Количество основных томов
        /// </summary>
        public int DataCount
        {
            get
            {                
                return _dataCount;                
            }

            set
            {
                _dataCount = value;
            }
        }

        /// <summary>
        /// Количество томов для восстановления
        /// </summary>
        public int EccCount
        {
            get
            {
                return _eccCount;
            }

            set
            {
                _eccCount = value;
            }
        }

        /// <summary>
        /// Используется режим восстановления данных?
        /// </summary>
        public RSModes Mode
        {
            get
            {
                return _mode;
            }

            set
            {
                _mode = value;
            }
        }
       
        #endregion Public Properties

        #region Data
        
        // Создаем экземпляр класса для упаковки (распаковки) имени файла в префиксный формат            
        FileNamer _eFileNamer;

        // Ядро системы отказоустойчивого кодирования
        RecoveryStarCore _eRecoveryStarCore;
       
        // Список полных имен файлов для обработки
        private ArrayList _filenamesToProcess;

        // Количество основных томов
        private int _dataCount;

        // Количество томов для восстановления
        private int _eccCount;

        // Используется режим восстановления данных?
        private RSModes _mode;

        // Счетчик корректно обработанных файлов
        private int _OKCount;
        
        // Счетчик некорректно обработанных файлов
        private int _errorCount;

        // Поток обработки данных
        private Thread _thrRecoveryStarProcess;

        // Приоритет процесса обработки данных
        private ThreadPriority _threadPriority;

        // Событие прекращения обработки
        private ManualResetEvent[] _exitEvent;

        // Событие продолжения обработки
        private ManualResetEvent[] _executeEvent;

        // Событие "пробуждения" цикла ожидания
        private ManualResetEvent[] _wakeUpEvent;
  
        #endregion Data        
    }        
}