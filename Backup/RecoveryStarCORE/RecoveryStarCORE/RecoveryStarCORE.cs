/*----------------------------------------------------------------------+
 |	filename:	RecoveryStarCore.cs                                     |
 |----------------------------------------------------------------------|
 |	version:	1.14                                                    |
 |	revision:   02/03/2007	08:46                                       |
 |  author:		Дробанов Артём Федорович (DrAF)                         |
 |	e-mail:		draf@mail.ru                                            |
 |	purpose:	Работа с файлами                                        |
 +----------------------------------------------------------------------+
 |  Отказоустойчивое кодирование по типу RAID-систем                    |
 +----------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.IO;

namespace RecoveryStar
{  
    /// <summary>
    /// Класс для кодирования файлов в RAID-подобном формате
    /// </summary>
    public class RecoveryStarCore
    {
        #region Construction & Destruction

        public RecoveryStarCore()
        {
            // Модуль для упаковки (распаковки) имени файла в префиксный формат
            _eFileNamer = new FileNamer();

             // Модуль вычисления и контроля сигнатуры целостности файла CRC-64
            _eFileAnalyzer = new FileAnalyzer();
        
            // RAID-подобный файловый кодер
            _eFileCodec = new FileCodec();
          
            // Модуль разбиения (склеивания) файлов на тома
            _eFileSplitter = new FileSplitter();

            // Экземляр класса полностью закончил обработку?
            _finished = true;

            // Обработка произведена корректно?
            _processedOK = false;

            // По-умолчанию устанавливается фоновый приоритет
            _threadPriority = 0;

            // Инициализируем событие прекращения обработки файла
            _exitEvent = new ManualResetEvent[] { new ManualResetEvent(false) };

            // Инициализируем cобытие продолжения обработки файла
            _executeEvent = new ManualResetEvent[] { new ManualResetEvent(false) };

            // Инициализируем cобытие "пробуждения" цикла ожидания
            _wakeUpEvent = new ManualResetEvent[] { new ManualResetEvent(false) };
         
            // Событие, устанавливаемое по завершении обработки
            _finishedEvent = new ManualResetEvent[] { new ManualResetEvent(true) };
        }

        #endregion Construction & Destruction

        #region Delegates

        /// <summary>
        /// Делегат обновления прогресса разбиения (склеивания) файла
        /// </summary>        
        public OnUpdateFileSplittingProgressHandler OnUpdateFileSplittingProgress;

        /// <summary>
        /// Делегат завершения процесса разбиения (склеивания) файла
        /// </summary>
        public OnFileSplittingFinishHandler OnFileSplittingFinish;

        /// <summary>
        /// Делегат обновления процесса формирования матрицы "F"
        /// </summary>        
        public OnUpdateRSMatrixFormingProgressHandler OnUpdateRSMatrixFormingProgress;

        /// <summary>
        /// Делегат завершения процесса формирования матрицы "F"
        /// </summary>
        public OnRSMatrixFormingFinishHandler OnRSMatrixFormingFinish;

        /// <summary>
        /// Делегат обновления прогресса кодирования файлов
        /// </summary>        
        public OnUpdateFileCodingProgressHandler OnUpdateFileCodingProgress;

        /// <summary>
        /// Делегат завершения процесса разбиения (склеивания) файла
        /// </summary>
        public OnFileCodingFinishHandler OnFileCodingFinish;
   
        /// <summary>
        /// Делегат обновления процесса контроля целостности файлов
        /// </summary>        
        public OnUpdateFileAnalyzeProgressHandler OnUpdateFileAnalyzeProgress;

        /// <summary>
        /// Делегат завершения процесса контроля целостности файлов
        /// </summary>
        public OnFileAnalyzeFinishHandler OnFileAnalyzeFinish;

        /// <summary>
        /// Делегат получения статистики повреждений многотомного архива
        /// </summary>        
        public OnGetDamageStatHandler OnGetDamageStat;

        #endregion Delegates

        #region Public Operations

        /// <summary>
        /// Отказоустойчивое кодирование файла по типу RAID
        /// </summary>        
        /// <param name="fullFilename">Полное имя файла для отказоустойчивого кодирования</param>
        /// <param name="dataCount">Количество основных томов</param>
        /// <param name="eccCount">Количество томов для восстановления</param>
        /// <param name="runAsSeparateThread">Запускать в отдельном потоке?</param>
        /// <returns>Булевский флаг операции</returns>
        public bool StartToProtect(String fullFilename, int dataCount, int eccCount, bool runAsSeparateThread)
        {
            // Если поток кодирования файла работает - не позволяем повторный запуск        
            if (InProcessing)
            {
                return false;
            }

            // Сбрасываем флаг корректности результата перед запуском потока
            _processedOK = false;

            // Сбрасываем индикатор актуального состояния переменных-членов
            _finished = false;
                        
            // Если имя файла не установлено
            if (
                    (fullFilename == null)
                 ||
                    (fullFilename == "")
                )
            {
                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return false;
            }

            // Производим выделение пути из полного имени файла
            _path = _eFileNamer.GetPath(fullFilename);

            // Производим выделение имени из полного имени файла
            _filename = _eFileNamer.GetShortFilename(fullFilename);

            // Если исходный файл не существует, сообщаем об ошибке
            if (!File.Exists(_path + _filename))
            {
                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return false;
            }
          
            // Сохраняем количество основных томов
            _dataCount = dataCount;

            // Сохраняем количество томов для восстановления
            _eccCount = eccCount;

            // Подписываемся на делегатов
            _eFileSplitter.OnUpdateFileSplittingProgress = OnUpdateFileSplittingProgress;
            _eFileSplitter.OnFileSplittingFinish = OnFileSplittingFinish;

            _eFileCodec.OnUpdateRSMatrixFormingProgress = OnUpdateRSMatrixFormingProgress;
            _eFileCodec.OnRSMatrixFormingFinish = OnRSMatrixFormingFinish;
            _eFileCodec.OnUpdateFileCodingProgress = OnUpdateFileCodingProgress;
            _eFileCodec.OnFileCodingFinish = OnFileCodingFinish;
      
            _eFileAnalyzer.OnUpdateFileAnalyzeProgress = OnUpdateFileAnalyzeProgress;
            _eFileAnalyzer.OnFileAnalyzeFinish = OnFileAnalyzeFinish;
                                 
            // Указываем, что поток должен выполняться
            _exitEvent[0].Reset();
            _executeEvent[0].Set();
            _finishedEvent[0].Reset();

            // Если указано, что не требуется запуск в отдельном потоке,
            // запускаем в данном
            if (!runAsSeparateThread)
            {
                // Защищаем файл от повреждений (кодируем его)
                Protect();
                
                // Возвращаем результат обработки
                return _processedOK;
            }
           
            // Создаем поток кодирования файлов...
            _thrFileCodingProc = new Thread(new ThreadStart(Protect));

            //...затем даем ему имя...
            _thrFileCodingProc.Name = "FileCodec.Protect()";

            //...устанавливаем выбранный приоритет задачи...
            _thrFileCodingProc.Priority = _threadPriority;

            //...и запускаем его
            _thrFileCodingProc.Start();

            // Сообщаем, что все нормально
            return true;
        }

        /// <summary>
        /// Отказоустойчивое декодирование файла
        /// </summary>        
        /// <param name="fullFilename">Полное имя файла для восстановления</param>
        /// <param name="runAsSeparateThread">Запускать в отдельном потоке?</param>
        /// <returns>Булевский флаг операции</returns>
        public bool StartToRecover(String fullFilename, bool runAsSeparateThread)
        {
            // Если поток кодирования файла работает - не позволяем повторный запуск        
            if (InProcessing)
            {
                return false;
            }

            // Сбрасываем флаг корректности результата перед запуском потока
            _processedOK = false;

            // Сбрасываем индикатор актуального состояния переменных-членов
            _finished = false;
                        
            // Если имя файла не установлено
            if (
                    (fullFilename == null)
                 ||
                    (fullFilename == "")
                )
            {
                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return false;
            }

            // Производим выделение пути из полного имени файла
            _path = _eFileNamer.GetPath(fullFilename);

            // Производим выделение имени из полного имени файла
            _filename = _eFileNamer.GetShortFilename(fullFilename);

            // Если исходный файл не существует, сообщаем об ошибке
            if (!File.Exists(_path + _filename))
            {
                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return false;
            }

            // Распаковываем исходное имя файла из префиксного формата,
            // и в результате получаем параметры "_filename", "_dataCount", "_eccCount"
            if (!_eFileNamer.Unpack(ref _filename, ref _dataCount, ref _eccCount))
            {
                return false;
            }
           
            // Подписываемся на делегатов
            _eFileSplitter.OnUpdateFileSplittingProgress = OnUpdateFileSplittingProgress;
            _eFileSplitter.OnFileSplittingFinish = OnFileSplittingFinish;

            _eFileCodec.OnUpdateRSMatrixFormingProgress = OnUpdateRSMatrixFormingProgress;
            _eFileCodec.OnRSMatrixFormingFinish = OnRSMatrixFormingFinish;
            _eFileCodec.OnUpdateFileCodingProgress = OnUpdateFileCodingProgress;
            _eFileCodec.OnFileCodingFinish = OnFileCodingFinish;

            _eFileAnalyzer.OnUpdateFileAnalyzeProgress = OnUpdateFileAnalyzeProgress;
            _eFileAnalyzer.OnFileAnalyzeFinish = OnFileAnalyzeFinish;
            _eFileAnalyzer.OnGetDamageStat = OnGetDamageStat;
           
            // Указываем, что поток должен выполняться
            _exitEvent[0].Reset();
            _executeEvent[0].Set();
            _finishedEvent[0].Reset();

            // Если указано, что не требуется запуск в отдельном потоке,
            // запускаем в данном
            if (!runAsSeparateThread)
            {
                // Восстанавливаем файл из многотомного архива с коррекцией ошибок
                Recover();
                
                // Возвращаем результат обработки
                return _processedOK;
            }
            
            // Создаем поток восстановления файлов...
            _thrFileCodingProc = new Thread(new ThreadStart(Recover));

            //...затем даем ему имя...
            _thrFileCodingProc.Name = "FileCodec.Recover()";

            //...устанавливаем выбранный приоритет задачи...
            _thrFileCodingProc.Priority = _threadPriority;

            //...и запускаем его
            _thrFileCodingProc.Start();

            // Сообщаем, что все нормально
            return true;
        }

        /// <summary>
        /// Восстановление отказоустойчивого набора данных
        /// </summary>
        /// <param name="fullFilename">Полное имя файла для восстановления</param>
        /// <param name="runAsSeparateThread">Запускать в отдельном потоке?</param>
        /// <returns>Булевский флаг операции</returns>
        public bool StartToRepair(String fullFilename, bool runAsSeparateThread)
        {
            // Если поток кодирования файла работает - не позволяем повторный запуск        
            if (InProcessing)
            {
                return false;
            }

            // Сбрасываем флаг корректности результата перед запуском потока
            _processedOK = false;

            // Сбрасываем индикатор актуального состояния переменных-членов
            _finished = false;

            // Если имя файла не установлено
            if (
                    (fullFilename == null)
                 ||
                    (fullFilename == "")
                )
            {
                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return false;
            }

            // Производим выделение пути из полного имени файла
            _path = _eFileNamer.GetPath(fullFilename);

            // Производим выделение имени из полного имени файла
            _filename = _eFileNamer.GetShortFilename(fullFilename);

            // Если исходный файл не существует, сообщаем об ошибке
            if (!File.Exists(_path + _filename))
            {
                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return false;
            }

            // Распаковываем исходное имя файла из префиксного формата,
            // и в результате получаем параметры "_filename", "_dataCount", "_eccCount"
            if (!_eFileNamer.Unpack(ref _filename, ref _dataCount, ref _eccCount))
            {
                return false;
            }

            // Подписываемся на делегатов
            _eFileCodec.OnUpdateRSMatrixFormingProgress = OnUpdateRSMatrixFormingProgress;
            _eFileCodec.OnRSMatrixFormingFinish = OnRSMatrixFormingFinish;
            _eFileCodec.OnUpdateFileCodingProgress = OnUpdateFileCodingProgress;
            _eFileCodec.OnFileCodingFinish = OnFileCodingFinish;

            _eFileAnalyzer.OnUpdateFileAnalyzeProgress = OnUpdateFileAnalyzeProgress;
            _eFileAnalyzer.OnFileAnalyzeFinish = OnFileAnalyzeFinish;
            _eFileAnalyzer.OnGetDamageStat = OnGetDamageStat;

            // Указываем, что поток должен выполняться
            _exitEvent[0].Reset();
            _executeEvent[0].Set();
            _finishedEvent[0].Reset();

            // Если указано, что не требуется запуск в отдельном потоке,
            // запускаем в данном
            if (!runAsSeparateThread)
            {
                // Восстанавливаем файл из многотомного архива с коррекцией ошибок
                Repair();

                // Возвращаем результат обработки
                return _processedOK;
            }

            // Создаем поток восстановления файлов...
            _thrFileCodingProc = new Thread(new ThreadStart(Repair));

            //...затем даем ему имя...
            _thrFileCodingProc.Name = "FileCodec.Repair()";

            //...устанавливаем выбранный приоритет задачи...
            _thrFileCodingProc.Priority = _threadPriority;

            //...и запускаем его
            _thrFileCodingProc.Start();

            // Сообщаем, что все нормально
            return true;
        }

        /// <summary>
        /// Тестирование отказоустойчивого набора данных
        /// </summary>
        /// <param name="fullFilename">Полное имя файла для тестирования</param>
        /// <param name="runAsSeparateThread">Запускать в отдельном потоке?</param>
        /// <returns>Булевский флаг операции</returns>
        public bool StartToTest(String fullFilename, bool runAsSeparateThread)
        {
            // Если поток кодирования файла работает - не позволяем повторный запуск        
            if (InProcessing)
            {
                return false;
            }

            // Сбрасываем флаг корректности результата перед запуском потока
            _processedOK = false;

            // Сбрасываем индикатор актуального состояния переменных-членов
            _finished = false;

            // Если имя файла не установлено
            if (
                    (fullFilename == null)
                 ||
                    (fullFilename == "")
                )
            {
                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return false;
            }

            // Производим выделение пути из полного имени файла
            _path = _eFileNamer.GetPath(fullFilename);

            // Производим выделение имени из полного имени файла
            _filename = _eFileNamer.GetShortFilename(fullFilename);

            // Если исходный файл не существует, сообщаем об ошибке
            if (!File.Exists(_path + _filename))
            {
                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return false;
            }

            // Распаковываем исходное имя файла из префиксного формата,
            // и в результате получаем параметры "_filename", "_dataCount", "_eccCount"
            if (!_eFileNamer.Unpack(ref _filename, ref _dataCount, ref _eccCount))
            {
                return false;
            }

            // Подписываемся на делегатов          
            _eFileAnalyzer.OnUpdateFileAnalyzeProgress = OnUpdateFileAnalyzeProgress;
            _eFileAnalyzer.OnFileAnalyzeFinish = OnFileAnalyzeFinish;
            _eFileAnalyzer.OnGetDamageStat = OnGetDamageStat;

            // Указываем, что поток должен выполняться
            _exitEvent[0].Reset();
            _executeEvent[0].Set();
            _finishedEvent[0].Reset();

            // Если указано, что не требуется запуск в отдельном потоке,
            // запускаем в данном
            if (!runAsSeparateThread)
            {
                // Восстанавливаем файл из многотомного архива с коррекцией ошибок
                Test();

                // Возвращаем результат обработки
                return _processedOK;
            }

            // Создаем поток восстановления файлов...
            _thrFileCodingProc = new Thread(new ThreadStart(Test));

            //...затем даем ему имя...
            _thrFileCodingProc.Name = "FileCodec.Test()";

            //...устанавливаем выбранный приоритет задачи...
            _thrFileCodingProc.Priority = _threadPriority;

            //...и запускаем его
            _thrFileCodingProc.Start();

            // Сообщаем, что все нормально
            return true;
        }

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
        /// Отказоустойчивое кодирование файла по типу RAID
        /// </summary>
        private void Protect()
        {                      
            // Разбиваем исходный файл на фрагменты
            if (_eFileSplitter.StartToSplit(_path, _filename, _dataCount, _eccCount, true))
            {
                // Цикл ожидания завершения этапа разбиения исходного файла на тома              
                while (true)
                {
                    // Если не обнаружили установленного события "_executeEvent",
                    // то пользователь хочет, чтобы мы поставили обработку на паузу -                       
                    if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                    {
                        //...приостанавливаем работу контролируемого алгоритма...                      
                        _eFileSplitter.Pause();

                        //...и сами засыпаем
                        ManualResetEvent.WaitAll(_executeEvent);

                        // А когда проснулись, указываем, что обработка должна продолжаться                            
                        _eFileSplitter.Continue();
                    }

                    // Ждем любое из перечисленных событий...
                    int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileSplitter.FinishedEvent[0] });

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
                        _eFileSplitter.Stop();

                        // Указываем на то, что обработка была прервана
                        _processedOK = false;

                        // Активируем индикатор актуального состояния переменных-членов
                        _finished = true;

                        // Устанавливаем событие завершения обработки
                        _finishedEvent[0].Set();

                        return;
                    }

                    //...если получили сигнал о завершении обработки вложенным алгоритмом...
                    if (eventIdx == 2)
                    {
                        //...выходим из цикла ожидания завершения (этого и ждали в while(true)!)
                        break;
                    }

                } // while(true)

            } else
            {
                // Сбрасываем флаг корректности результата
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }

            // В связи с закрытием большого количества файловых потоков
            // необходимо дождаться записи изменений, внесенных потоком
            // кодирования в тело класса. Поток уже не работает, но
            // установленное им булевское свойство, возможно, ещё
            // "не проявилось". Достаточно необычная ситуация,
            // но факт - вещь упрямая.
            for (int i = 0; i < 9000; i++)
            {
                if (!_eFileSplitter.Finished)
                {
                    Thread.Sleep(100);
                }
            }

            // Если 15 минут ожидания закрытия файловых потоков не привели к желаемому
            // результату - это ошибка
            if (!_eFileSplitter.ProcessedOK)
            {
                // Указываем на то, что обработка была прервана
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }

            // Создаем тома для восстановления
            if (_eFileCodec.StartToEncode(_path, _filename, _dataCount, _eccCount, true))
            {
                // Цикл ожидания завершения этапа кодирования томов          
                while (true)
                {
                    // Если не обнаружили установленного события "_executeEvent",
                    // то пользователь хочет, чтобы мы поставили обработку на паузу -                       
                    if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                    {
                        //...приостанавливаем работу контролируемого алгоритма...                      
                        _eFileCodec.Pause();

                        //...и сами засыпаем
                        ManualResetEvent.WaitAll(_executeEvent);

                        // А когда проснулись, указываем, что обработка должна продолжаться                            
                        _eFileCodec.Continue();
                    }

                    // Ждем любое из перечисленных событий...
                    int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileCodec.FinishedEvent[0] });

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
                        _eFileCodec.Stop();

                        // Указываем на то, что обработка была прервана
                        _processedOK = false;

                        // Активируем индикатор актуального состояния переменных-членов
                        _finished = true;

                        // Устанавливаем событие завершения обработки
                        _finishedEvent[0].Set();

                        return;
                    }

                    //...если получили сигнал о завершении обработки вложенным алгоритмом...
                    if (eventIdx == 2)
                    {
                        //...выходим из цикла ожидания завершения (этого и ждали в while(true)!)
                        break;
                    }

                } // while(true)

            } else
            {
                // Сбрасываем флаг корректности результата
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }

            // В связи с закрытием большого количества файловых потоков
            // необходимо дождаться записи изменений, внесенных потоком
            // кодирования в тело класса. Поток уже не работает, но
            // установленное им булевское свойство, возможно, ещё
            // "не проявилось". Достаточно необычная ситуация,
            // но факт - вещь упрямая.
            for (int i = 0; i < 9000; i++)
            {
                if (!_eFileCodec.Finished)
                {
                    Thread.Sleep(100);
                }
            }

            // Если 15 минут ожидания закрытия файловых потоков не привели к желаемому
            // результату - это ошибка
            if (!_eFileCodec.ProcessedOK)
            {
                // Указываем на то, что обработка была прервана
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }

            // Осуществляем вычисление сигнатур целостности CRC-64 для всего набора томов
            if (_eFileAnalyzer.StartToWriteCRC64(_path, _filename, _dataCount, _eccCount, true))
            {
                // Цикл ожидания завершения процесса расчета сигнатур целостности томов                
                while (true)
                {
                    // Если не обнаружили установленного события "_executeEvent",
                    // то пользователь хочет, чтобы мы поставили обработку на паузу -                       
                    if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                    {
                        //...приостанавливаем работу контролируемого алгоритма...                      
                        _eFileAnalyzer.Pause();

                        //...и сами засыпаем
                        ManualResetEvent.WaitAll(_executeEvent);
                        
                        // А когда проснулись, указываем, что обработка должна продолжаться                            
                        _eFileAnalyzer.Continue();
                    }

                    // Ждем любое из перечисленных событий...
                    int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileAnalyzer.FinishedEvent[0] });

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
                        _eFileAnalyzer.Stop();

                        // Указываем на то, что обработка была прервана
                        _processedOK = false;

                        // Активируем индикатор актуального состояния переменных-членов
                        _finished = true;

                        // Устанавливаем событие завершения обработки
                        _finishedEvent[0].Set();

                        return;
                    }

                    //...если получили сигнал о завершении обработки вложенным алгоритмом...
                    if (eventIdx == 2)
                    {
                        //...выходим из цикла ожидания завершения (этого и ждали в while(true)!)
                        break;
                    }

                } // while(true)

            } else
            {
                // Сбрасываем флаг корректности результата
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();
                
                return;
            }

            // В связи с закрытием большого количества файловых потоков
            // необходимо дождаться записи изменений, внесенных потоком
            // кодирования в тело класса. Поток уже не работает, но
            // установленное им булевское свойство, возможно, ещё
            // "не проявилось". Достаточно необычная ситуация,
            // но факт - вещь упрямая.
            for (int i = 0; i < 9000; i++)
            {
                if (!_eFileAnalyzer.Finished)
                {
                    Thread.Sleep(100);
                }
            }

            // Если 15 минут ожидания закрытия файловых потоков не привели к желаемому
            // результату - это ошибка
            if (!_eFileAnalyzer.ProcessedOK)
            {
                // Указываем на то, что обработка была прервана
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }
            
            // Указываем на то, что обработка была произведена корректно
            _processedOK = true;

            // Активируем индикатор актуального состояния переменных-членов
            _finished = true;

            // Устанавливаем событие завершения обработки
            _finishedEvent[0].Set();
        }

        /// <summary>
        /// Декодирование последовательности файлов
        /// </summary>
        private void Recover()
        {
            // Список томов, используемых для восстановления
            int[] volList;
            
            // Осуществляем проверку сигнатур целостности CRC-64 для всего набора томов
            if (_eFileAnalyzer.StartToAnalyzeCRC64(_path, _filename, _dataCount, _eccCount, true))
            {
                // Цикл ожидания завершения процесса расчета сигнатур целостности томов
                while (true)
                {
                    // Если не обнаружили установленного события "_executeEvent",
                    // то пользователь хочет, чтобы мы поставили обработку на паузу -                       
                    if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                    {
                        //...приостанавливаем работу контролируемого алгоритма...                      
                        _eFileAnalyzer.Pause();

                        //...и сами засыпаем
                        ManualResetEvent.WaitAll(_executeEvent);

                        // А когда проснулись, указываем, что обработка должна продолжаться                            
                        _eFileAnalyzer.Continue();
                    }

                    // Ждем любое из перечисленных событий...
                    int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileAnalyzer.FinishedEvent[0] });

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
                        _eFileAnalyzer.Stop();

                        // Указываем на то, что обработка была прервана
                        _processedOK = false;

                        // Активируем индикатор актуального состояния переменных-членов
                        _finished = true;

                        // Устанавливаем событие завершения обработки
                        _finishedEvent[0].Set();

                        return;
                    }

                    //...если получили сигнал о завершении обработки вложенным алгоритмом...
                    if (eventIdx == 2)
                    {
                        //...выходим из цикла ожидания завершения (этого и ждали в while(true)!)
                        break;
                    }

                } // while(true)

            } else
            {
                // Сбрасываем флаг корректности результата
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }

            // В связи с закрытием большого количества файловых потоков
            // необходимо дождаться записи изменений, внесенных потоком
            // кодирования в тело класса. Поток уже не работает, но
            // установленное им булевское свойство, возможно, ещё
            // "не проявилось". Достаточно необычная ситуация,
            // но факт - вещь упрямая.
            for (int i = 0; i < 9000; i++)
            {
                if (!_eFileAnalyzer.Finished)
                {
                    Thread.Sleep(100);
                }
            }

            // Если 15 минут ожидания закрытия файловых потоков не привели к желаемому
            // результату - это ошибка
            if (!_eFileAnalyzer.ProcessedOK)
            {
                // Указываем на то, что обработка была прервана
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }

            // Теперь, когда обработка завершена, необходимо проанализировать
            // полученный вектор "volList"
            volList = _eFileAnalyzer.VolList;
                     
            // Изначально предполагаем, что восстановление данных не потребуется
            bool needToRecover = false;

            // Проверяем вектор на наличие в нем томов для восстановления
            for (int dataNum = 0; dataNum < _dataCount; dataNum++)
            {                
                // Если встретился том для восстановления, часть
                // основных томов повреждена и требуется применение "FileCodec"
                if (volList[dataNum] != dataNum)
                {
                    needToRecover = true;

                    break;
                }
            }

            // Если требуется восстановление основных томов, запускаем его
            if (needToRecover)
            {
                // Восстанавливаем утерянные основные тома
                if (_eFileCodec.StartToDecode(_path, _filename, _dataCount, _eccCount, volList, true))
                {                    
                    // Цикл ожидания завершения этапа декодирования томов          
                    while (true)
                    {
                        // Если не обнаружили установленного события "_executeEvent",
                        // то пользователь хочет, чтобы мы поставили обработку на паузу -                       
                        if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                        {
                            //...приостанавливаем работу контролируемого алгоритма...                      
                            _eFileCodec.Pause();

                            //...и сами засыпаем
                            ManualResetEvent.WaitAll(_executeEvent);

                            // А когда проснулись, указываем, что обработка должна продолжаться                            
                            _eFileCodec.Continue();
                        }

                        // Ждем любое из перечисленных событий...
                        int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileCodec.FinishedEvent[0] });

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
                            _eFileCodec.Stop();

                            // Указываем на то, что обработка была прервана
                            _processedOK = false;

                            // Активируем индикатор актуального состояния переменных-членов
                            _finished = true;

                            // Устанавливаем событие завершения обработки
                            _finishedEvent[0].Set();

                            return;
                        }

                        //...если получили сигнал о завершении обработки вложенным алгоритмом...
                        if (eventIdx == 2)
                        {
                            //...выходим из цикла ожидания завершения (этого и ждали в while(true)!)
                            break;
                        }

                    } // while(true)

                } else
                {
                    // Сбрасываем флаг корректности результата
                    _processedOK = false;

                    // Активируем индикатор актуального состояния переменных-членов
                    _finished = true;

                    // Устанавливаем событие завершения обработки
                    _finishedEvent[0].Set();

                    return;
                }

                // В связи с закрытием большого количества файловых потоков
                // необходимо дождаться записи изменений, внесенных потоком
                // кодирования в тело класса. Поток уже не работает, но
                // установленное им булевское свойство, возможно, ещё
                // "не проявилось". Достаточно необычная ситуация,
                // но факт - вещь упрямая.
                for (int i = 0; i < 9000; i++)
                {
                    if (!_eFileCodec.Finished)
                    {
                        Thread.Sleep(100);
                    }
                }

                // Если 15 минут ожидания закрытия файловых потоков не привели к желаемому
                // результату - это ошибка
                if (!_eFileCodec.ProcessedOK)
                {
                    // Указываем на то, что обработка была прервана
                    _processedOK = false;

                    // Активируем индикатор актуального состояния переменных-членов
                    _finished = true;

                    // Устанавливаем событие завершения обработки
                    _finishedEvent[0].Set();

                    return;
                }
            }
                        
            // Склеиваем исходный файл из восстановленных основных томов
            if (_eFileSplitter.StartToGlue(_path, _filename, _dataCount, _eccCount, true))
            {
                // Цикл ожидания завершения этапа склеивания исходного файла из томов                
                while (true)
                {
                    // Если не обнаружили установленного события "_executeEvent",
                    // то пользователь хочет, чтобы мы поставили обработку на паузу -                       
                    if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                    {
                        //...приостанавливаем работу контролируемого алгоритма...                      
                        _eFileSplitter.Pause();

                        //...и сами засыпаем
                        ManualResetEvent.WaitAll(_executeEvent);

                        // А когда проснулись, указываем, что обработка должна продолжаться                            
                        _eFileSplitter.Continue();
                    }

                    // Ждем любое из перечисленных событий...
                    int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileSplitter.FinishedEvent[0] });

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
                        _eFileSplitter.Stop();

                        // Указываем на то, что обработка была прервана
                        _processedOK = false;

                        // Активируем индикатор актуального состояния переменных-членов
                        _finished = true;

                        // Устанавливаем событие завершения обработки
                        _finishedEvent[0].Set();

                        return;
                    }

                    //...если получили сигнал о завершении обработки вложенным алгоритмом...
                    if (eventIdx == 2)
                    {
                        //...выходим из цикла ожидания завершения (этого и ждали в while(true)!)
                        break;
                    }

                } // while(true)

            } else
            {
                // Сбрасываем флаг корректности результата
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }

            // В связи с закрытием большого количества файловых потоков
            // необходимо дождаться записи изменений, внесенных потоком
            // кодирования в тело класса. Поток уже не работает, но
            // установленное им булевское свойство, возможно, ещё
            // "не проявилось". Достаточно необычная ситуация,
            // но факт - вещь упрямая.
            for (int i = 0; i < 9000; i++)
            {
                if (!_eFileSplitter.Finished)
                {
                    Thread.Sleep(100);
                }
            }

            // Если 15 минут ожидания закрытия файловых потоков не привели к желаемому
            // результату - это ошибка
            if (!_eFileSplitter.ProcessedOK)
            {
                // Указываем на то, что обработка была прервана
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }

            // Указываем на то, что обработка была произведена корректно
            _processedOK = true;

            // Активируем индикатор актуального состояния переменных-членов
            _finished = true;

            // Устанавливаем событие завершения обработки
            _finishedEvent[0].Set();
        }

        /// <summary>
        /// "Лечение" набора файлов
        /// </summary>
        private void Repair()
        {
            // Список томов, используемых для восстановления
            int[] volList;

            // Осуществляем проверку сигнатур целостности CRC-64 для всего набора томов
            if (_eFileAnalyzer.StartToAnalyzeCRC64(_path, _filename, _dataCount, _eccCount, true))
            {
                // Цикл ожидания завершения процесса расчета сигнатур целостности томов
                while (true)
                {
                    // Если не обнаружили установленного события "_executeEvent",
                    // то пользователь хочет, чтобы мы поставили обработку на паузу -                       
                    if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                    {
                        //...приостанавливаем работу контролируемого алгоритма...                      
                        _eFileAnalyzer.Pause();

                        //...и сами засыпаем
                        ManualResetEvent.WaitAll(_executeEvent);

                        // А когда проснулись, указываем, что обработка должна продолжаться                            
                        _eFileAnalyzer.Continue();
                    }

                    // Ждем любое из перечисленных событий...
                    int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileAnalyzer.FinishedEvent[0] });

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
                        _eFileAnalyzer.Stop();

                        // Указываем на то, что обработка была прервана
                        _processedOK = false;

                        // Активируем индикатор актуального состояния переменных-членов
                        _finished = true;

                        // Устанавливаем событие завершения обработки
                        _finishedEvent[0].Set();

                        return;
                    }

                    //...если получили сигнал о завершении обработки вложенным алгоритмом...
                    if (eventIdx == 2)
                    {
                        //...выходим из цикла ожидания завершения (этого и ждали в while(true)!)
                        break;
                    }

                } // while(true)

            } else
            {
                // Сбрасываем флаг корректности результата
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }

            // В связи с закрытием большого количества файловых потоков
            // необходимо дождаться записи изменений, внесенных потоком
            // кодирования в тело класса. Поток уже не работает, но
            // установленное им булевское свойство, возможно, ещё
            // "не проявилось". Достаточно необычная ситуация,
            // но факт - вещь упрямая.
            for (int i = 0; i < 9000; i++)
            {
                if (!_eFileAnalyzer.Finished)
                {
                    Thread.Sleep(100);
                }
            }

            // Если 15 минут ожидания закрытия файловых потоков не привели к желаемому
            // результату - это ошибка
            if (!_eFileAnalyzer.ProcessedOK)
            {
                // Указываем на то, что обработка была прервана
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }

            // Теперь, когда обработка завершена, необходимо проанализировать
            // полученный вектор "volList"
            volList = _eFileAnalyzer.VolList;

            // Изначально предполагаем, что восстановление данных не потребуется
            bool needToRecover = false;

            // Проверяем вектор на наличие в нем томов для восстановления
            for (int dataNum = 0; dataNum < _dataCount; dataNum++)
            {
                // Если встретился том для восстановления, часть
                // основных томов повреждена и требуется применение "FileCodec"
                if (volList[dataNum] != dataNum)
                {
                    needToRecover = true;

                    break;
                }
            }

            // Если требуется восстановление основных томов, запускаем его
            if (needToRecover)
            {
                // Восстанавливаем утерянные основные тома
                if (_eFileCodec.StartToDecode(_path, _filename, _dataCount, _eccCount, volList, true))
                {
                    // Цикл ожидания завершения этапа декодирования томов          
                    while (true)
                    {
                        // Если не обнаружили установленного события "_executeEvent",
                        // то пользователь хочет, чтобы мы поставили обработку на паузу -                       
                        if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                        {
                            //...приостанавливаем работу контролируемого алгоритма...                      
                            _eFileCodec.Pause();

                            //...и сами засыпаем
                            ManualResetEvent.WaitAll(_executeEvent);

                            // А когда проснулись, указываем, что обработка должна продолжаться                            
                            _eFileCodec.Continue();
                        }

                        // Ждем любое из перечисленных событий...
                        int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileCodec.FinishedEvent[0] });

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
                            _eFileCodec.Stop();

                            // Указываем на то, что обработка была прервана
                            _processedOK = false;

                            // Активируем индикатор актуального состояния переменных-членов
                            _finished = true;

                            // Устанавливаем событие завершения обработки
                            _finishedEvent[0].Set();

                            return;
                        }

                        //...если получили сигнал о завершении обработки вложенным алгоритмом...
                        if (eventIdx == 2)
                        {
                            //...выходим из цикла ожидания завершения (этого и ждали в while(true)!)
                            break;
                        }

                    } // while(true)

                } else
                {
                    // Сбрасываем флаг корректности результата
                    _processedOK = false;

                    // Активируем индикатор актуального состояния переменных-членов
                    _finished = true;

                    // Устанавливаем событие завершения обработки
                    _finishedEvent[0].Set();

                    return;
                }

                // В связи с закрытием большого количества файловых потоков
                // необходимо дождаться записи изменений, внесенных потоком
                // кодирования в тело класса. Поток уже не работает, но
                // установленное им булевское свойство, возможно, ещё
                // "не проявилось". Достаточно необычная ситуация,
                // но факт - вещь упрямая.
                for (int i = 0; i < 9000; i++)
                {
                    if (!_eFileCodec.Finished)
                    {
                        Thread.Sleep(100);
                    }
                }

                // Если 15 минут ожидания закрытия файловых потоков не привели к желаемому
                // результату - это ошибка
                if (!_eFileCodec.ProcessedOK)
                {
                    // Указываем на то, что обработка была прервана
                    _processedOK = false;

                    // Активируем индикатор актуального состояния переменных-членов
                    _finished = true;

                    // Устанавливаем событие завершения обработки
                    _finishedEvent[0].Set();

                    return;
                }
            }
          
            // Файловый поток (необходим для того, чтобы укоротить каждый из файлов набора)            
            FileStream eFileStream = null;

            try
            {
                // Имя файла для обработки
                String filename;

                // Обрабатываем все файлы
                for (int i = 0; i < (_dataCount + _eccCount); i++)
                {
                    // Считываем первоначальное имя файла,...
                    filename = _filename;

                    //...упаковываем его в префиксный формат...
                    _eFileNamer.Pack(ref filename, i, _dataCount, _eccCount);

                    //...формируем полное имя файла...
                    filename = _path + filename;

                    //...производим тест на наличие файла...                    
                    if (File.Exists(filename))
                    {
                        //...если таковой имеется, ставим на него атрибуты
                        // по-умолчанию
                        File.SetAttributes(filename, FileAttributes.Normal);

                        //...открываем файловый поток на запись...
                        eFileStream = new FileStream(filename, FileMode.Open, System.IO.FileAccess.Write);

                        if (eFileStream != null)
                        {
                            //...укорачиваем его ровно на 8 байт (убирая CRC-64)...
                            eFileStream.SetLength(eFileStream.Length - 8);

                            //...сливаем файловый буфер...
                            eFileStream.Flush();

                            //...и закрываем файл
                            eFileStream.Close();

                            // Если закрыли поток - присваиваем ему null, чтобы в случае
                            // исключительной ситуации корректно распознавать неоткрытые потоки                        
                            eFileStream = null;
                        }
                    }
                }             
            }

            // Если было хотя бы одно исключение - закрываем файловый поток и
            // сообщаем об ошибке
            catch (IOException e)
            {
                // Закрываем файловый поток
                if (eFileStream != null)
                {
                    eFileStream.Close();
                    eFileStream = null;
                }

                // Указываем на то, что процесс "лечения" набора файлов прошел некорректно
                _processedOK = false;

                // Устанавливаем индикатор актуального состояния переменных-членов
                _finished = true;

                return;
            }

            // Если в результате анализа набора томов было установлено, что
            // все тома для восстановления являются неповрежденными,
            // нет потребности в их повторном создании
            if (!_eFileAnalyzer.AllEccVolsOK)
            {
                // Создаем тома для восстановления
                if (_eFileCodec.StartToEncode(_path, _filename, _dataCount, _eccCount, true))
                {
                    // Цикл ожидания завершения этапа кодирования томов          
                    while (true)
                    {
                        // Если не обнаружили установленного события "_executeEvent",
                        // то пользователь хочет, чтобы мы поставили обработку на паузу -                       
                        if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                        {
                            //...приостанавливаем работу контролируемого алгоритма...                      
                            _eFileCodec.Pause();

                            //...и сами засыпаем
                            ManualResetEvent.WaitAll(_executeEvent);

                            // А когда проснулись, указываем, что обработка должна продолжаться                            
                            _eFileCodec.Continue();
                        }

                        // Ждем любое из перечисленных событий...
                        int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileCodec.FinishedEvent[0] });

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
                            _eFileCodec.Stop();

                            // Указываем на то, что обработка была прервана
                            _processedOK = false;

                            // Активируем индикатор актуального состояния переменных-членов
                            _finished = true;

                            // Устанавливаем событие завершения обработки
                            _finishedEvent[0].Set();

                            return;
                        }

                        //...если получили сигнал о завершении обработки вложенным алгоритмом...
                        if (eventIdx == 2)
                        {
                            //...выходим из цикла ожидания завершения (этого и ждали в while(true)!)
                            break;
                        }

                    } // while(true)

                } else
                {
                    // Сбрасываем флаг корректности результата
                    _processedOK = false;

                    // Активируем индикатор актуального состояния переменных-членов
                    _finished = true;

                    // Устанавливаем событие завершения обработки
                    _finishedEvent[0].Set();

                    return;
                }

                // В связи с закрытием большого количества файловых потоков
                // необходимо дождаться записи изменений, внесенных потоком
                // кодирования в тело класса. Поток уже не работает, но
                // установленное им булевское свойство, возможно, ещё
                // "не проявилось". Достаточно необычная ситуация,
                // но факт - вещь упрямая.
                for (int i = 0; i < 9000; i++)
                {
                    if (!_eFileCodec.Finished)
                    {
                        Thread.Sleep(100);
                    }
                }

                // Если 15 минут ожидания закрытия файловых потоков не привели к желаемому
                // результату - это ошибка
                if (!_eFileCodec.ProcessedOK)
                {
                    // Указываем на то, что обработка была прервана
                    _processedOK = false;

                    // Активируем индикатор актуального состояния переменных-членов
                    _finished = true;

                    // Устанавливаем событие завершения обработки
                    _finishedEvent[0].Set();

                    return;
                }
            }

            // Осуществляем вычисление сигнатур целостности CRC-64 для всего набора томов
            if (_eFileAnalyzer.StartToWriteCRC64(_path, _filename, _dataCount, _eccCount, true))
            {
                // Цикл ожидания завершения процесса расчета сигнатур целостности томов                
                while (true)
                {
                    // Если не обнаружили установленного события "_executeEvent",
                    // то пользователь хочет, чтобы мы поставили обработку на паузу -                       
                    if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                    {
                        //...приостанавливаем работу контролируемого алгоритма...                      
                        _eFileAnalyzer.Pause();

                        //...и сами засыпаем
                        ManualResetEvent.WaitAll(_executeEvent);

                        // А когда проснулись, указываем, что обработка должна продолжаться                            
                        _eFileAnalyzer.Continue();
                    }

                    // Ждем любое из перечисленных событий...
                    int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileAnalyzer.FinishedEvent[0] });

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
                        _eFileAnalyzer.Stop();

                        // Указываем на то, что обработка была прервана
                        _processedOK = false;

                        // Активируем индикатор актуального состояния переменных-членов
                        _finished = true;

                        // Устанавливаем событие завершения обработки
                        _finishedEvent[0].Set();

                        return;
                    }

                    //...если получили сигнал о завершении обработки вложенным алгоритмом...
                    if (eventIdx == 2)
                    {
                        //...выходим из цикла ожидания завершения (этого и ждали в while(true)!)
                        break;
                    }

                } // while(true)

            } else
            {
                // Сбрасываем флаг корректности результата
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }

            // В связи с закрытием большого количества файловых потоков
            // необходимо дождаться записи изменений, внесенных потоком
            // кодирования в тело класса. Поток уже не работает, но
            // установленное им булевское свойство, возможно, ещё
            // "не проявилось". Достаточно необычная ситуация,
            // но факт - вещь упрямая.
            for (int i = 0; i < 9000; i++)
            {
                if (!_eFileAnalyzer.Finished)
                {
                    Thread.Sleep(100);
                }
            }

            // Если 15 минут ожидания закрытия файловых потоков не привели к желаемому
            // результату - это ошибка
            if (!_eFileAnalyzer.ProcessedOK)
            {
                // Указываем на то, что обработка была прервана
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }

            // Указываем на то, что обработка была произведена корректно
            _processedOK = true;

            // Активируем индикатор актуального состояния переменных-членов
            _finished = true;

            // Устанавливаем событие завершения обработки
            _finishedEvent[0].Set();
        }

        /// <summary>
        /// Тестирование набора файлов
        /// </summary>
        private void Test()
        {
            // Осуществляем проверку сигнатур целостности CRC-64 для всего набора томов
            if (_eFileAnalyzer.StartToAnalyzeCRC64(_path, _filename, _dataCount, _eccCount, true))
            {
                // Цикл ожидания завершения процесса расчета сигнатур целостности томов
                while (true)
                {
                    // Если не обнаружили установленного события "_executeEvent",
                    // то пользователь хочет, чтобы мы поставили обработку на паузу -                       
                    if (!ManualResetEvent.WaitAll(_executeEvent, 0, false))
                    {
                        //...приостанавливаем работу контролируемого алгоритма...                      
                        _eFileAnalyzer.Pause();

                        //...и сами засыпаем
                        ManualResetEvent.WaitAll(_executeEvent);

                        // А когда проснулись, указываем, что обработка должна продолжаться                            
                        _eFileAnalyzer.Continue();
                    }

                    // Ждем любое из перечисленных событий...
                    int eventIdx = ManualResetEvent.WaitAny(new ManualResetEvent[] { _wakeUpEvent[0], _exitEvent[0], _eFileAnalyzer.FinishedEvent[0] });

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
                        _eFileAnalyzer.Stop();

                        // Указываем на то, что обработка была прервана
                        _processedOK = false;

                        // Активируем индикатор актуального состояния переменных-членов
                        _finished = true;

                        // Устанавливаем событие завершения обработки
                        _finishedEvent[0].Set();

                        return;
                    }

                    //...если получили сигнал о завершении обработки вложенным алгоритмом...
                    if (eventIdx == 2)
                    {
                        //...выходим из цикла ожидания завершения (этого и ждали в while(true)!)
                        break;
                    }

                } // while(true)

            } else
            {
                // Сбрасываем флаг корректности результата
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }

            // В связи с закрытием большого количества файловых потоков
            // необходимо дождаться записи изменений, внесенных потоком
            // кодирования в тело класса. Поток уже не работает, но
            // установленное им булевское свойство, возможно, ещё
            // "не проявилось". Достаточно необычная ситуация,
            // но факт - вещь упрямая.
            for (int i = 0; i < 9000; i++)
            {
                if (!_eFileAnalyzer.Finished)
                {
                    Thread.Sleep(100);
                }
            }

            // Если 15 минут ожидания закрытия файловых потоков не привели к желаемому
            // результату - это ошибка
            if (!_eFileAnalyzer.ProcessedOK)
            {
                // Указываем на то, что обработка была прервана
                _processedOK = false;

                // Активируем индикатор актуального состояния переменных-членов
                _finished = true;

                // Устанавливаем событие завершения обработки
                _finishedEvent[0].Set();

                return;
            }

            // Указываем на то, что обработка была произведена корректно
            _processedOK = true;

            // Активируем индикатор актуального состояния переменных-членов
            _finished = true;

            // Устанавливаем событие завершения обработки
            _finishedEvent[0].Set();
        }

        #endregion Private Operations

        #region Public Properties

        /// <summary>
        /// Булевское свойство "Файл обрабатывается?"
        /// </summary>        
        public bool InProcessing
        {
            get
            {                
                if (
                        (_thrFileCodingProc != null)
                     &&
                        (
                                (_thrFileCodingProc.ThreadState == ThreadState.Running)
                            ||
                                (_thrFileCodingProc.ThreadState == ThreadState.WaitSleepJoin)
                        )
                    )
                {
                    return true;

                } else
                {
                    return false;
                }
            }
        }
        
        /// <summary>
        /// Булевское свойство "Экземпляр класса закончил обработку
        /// (имеет актуальное состояние переменных-членов)?"
        /// </summary>
        public bool Finished
        {
            get
            {
                // Если класс не занят обработкой - возвращаем значение
                if (!InProcessing)
                {
                    return _finished;

                } else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Обработка файлов произведена корректно?"
        /// </summary>        
        public bool ProcessedOK
        {
            get
            {
                // Если класс не занят обработкой - возвращаем значение
                if (!InProcessing)
                {
                    return _processedOK;

                } else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Приоритет процесса
        /// </summary>
        public int ThreadPriority
        {
            get
            {
                return (int)_threadPriority;
            }

            set
            {
                if (
                        (_thrFileCodingProc != null)
                     &&
                        (_thrFileCodingProc.IsAlive)
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

                    // Устанавливаем выбранный приоритет процесса                
                    _thrFileCodingProc.Priority = _threadPriority;

                    if (_eFileAnalyzer != null)
                    {
                        _eFileAnalyzer.ThreadPriority = value;
                    }

                    if (_eFileCodec != null)
                    {
                        _eFileCodec.ThreadPriority = value;
                    }

                    if (_eFileSplitter != null)
                    {
                        _eFileSplitter.ThreadPriority = value;
                    }
                }
            }
        }

        /// <summary>
        /// Событие, устанавливаемое по завершении обработки
        /// </summary>
        public ManualResetEvent[] FinishedEvent
        {
            get
            {
                return _finishedEvent;
            }
        }

        #endregion Public Properties

        #region Data

        // Модуль для упаковки (распаковки) имени файла в префиксный формат
        private FileNamer _eFileNamer;

        // Модуль вычисления и контроля сигнатуры целостности файла CRC-64
        private FileAnalyzer _eFileAnalyzer;
        
        // RAID-подобный файловый кодер
        private FileCodec _eFileCodec;
       
        // Модуль разбиения (склеивания) файлов на тома
        private FileSplitter _eFileSplitter;

        // Количество основных томов
        private int _dataCount;

        // Количество томов для восстановления
        private int _eccCount;

        // Путь к файлам для обработки
        private String _path;

        // Имя исходного файла для обработки
        private String _filename;

        // Экземляр класса полностью закончил обработку?
        private bool _finished;

        // Обработка произведена корректно?
        private bool _processedOK;
        
        // Поток кодирования данных
        private Thread _thrFileCodingProc;

        // Приоритет процесса разбиения (склеивания) файла
        private ThreadPriority _threadPriority;

        // Событие прекращения обработки файла
        private ManualResetEvent[] _exitEvent;

        // Событие продолжения обработки файла
        private ManualResetEvent[] _executeEvent;

        // Событие "пробуждения" цикла ожидания
        private ManualResetEvent[] _wakeUpEvent;

        // Событие, устанавливаемое по завершении обработки
        private ManualResetEvent[] _finishedEvent;

        #endregion Data

    } // class RecoveryStarCore
}
