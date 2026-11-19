/**
 * GosTek — Официальный сайт
 * Интерактивная логика, переключение шаблонов, анимации и автоопределение ОС
 */

document.addEventListener('DOMContentLoaded', () => {
  initHeaderScroll();
  initMobileMenu();
  initOsDetection();
  initTemplatePlayground();
  initMockupTabs();
  initFaqAccordion();
  initDownloadHandlers();
  initCopyChecksum();
  initScrollAnimations();
});

/* 1. Хедер при прокрутке */
function initHeaderScroll() {
  const header = document.querySelector('.site-header');
  window.addEventListener('scroll', () => {
    if (window.scrollY > 20) {
      header.classList.add('scrolled');
    } else {
      header.classList.remove('scrolled');
    }
  });
}

/* 2. Мобильное меню */
function initMobileMenu() {
  const toggle = document.querySelector('.mobile-toggle');
  const menu = document.querySelector('.nav-menu');
  if (!toggle || !menu) return;

  toggle.addEventListener('click', () => {
    menu.classList.toggle('open');
    toggle.textContent = menu.classList.contains('open') ? '✕' : '☰';
  });

  menu.querySelectorAll('.nav-link').forEach(link => {
    link.addEventListener('click', () => {
      menu.classList.remove('open');
      toggle.textContent = '☰';
    });
  });
}

/* 3. Автоопределение операционной системы */
function initOsDetection() {
  const userAgent = navigator.userAgent || navigator.vendor || window.opera;
  let detectedOs = 'windows';

  if (/android/i.test(userAgent)) {
    detectedOs = 'android';
  } else if (/windows|win32/i.test(userAgent)) {
    detectedOs = 'windows';
  }

  // Обновляем бейдж и подсветку карточек
  const heroCtaPrimary = document.getElementById('hero-cta-primary');
  const heroCtaSecondary = document.getElementById('hero-cta-secondary');
  const cardWindows = document.getElementById('card-windows');
  const cardAndroid = document.getElementById('card-android');

  if (detectedOs === 'android') {
    if (heroCtaPrimary && heroCtaSecondary) {
      heroCtaPrimary.href = 'downloads/GosTek.apk';
      heroCtaPrimary.innerHTML = `
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M17.5 19H9a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2h8.5a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2Z"/><path d="M12 18h.01"/></svg>
        <span>Скачать для Android (.apk)</span>
      `;
      heroCtaSecondary.href = '#download';
      heroCtaSecondary.innerHTML = `<span>Версия для Windows</span>`;
    }
    if (cardAndroid) cardAndroid.classList.add('highlight');
    if (cardWindows) cardWindows.classList.remove('highlight');
  } else {
    // Windows по умолчанию
    if (cardWindows) cardWindows.classList.add('highlight');
  }
}

/* 4. Интерактивный симулятор шаблонов */
const TEMPLATE_DATA = {
  academic: {
    header: `Ректору ФГБОУ ВО «Технический университет»<br>д.т.н., профессору Иванову И. И.<br>студента гр. ИВТ-401<br><strong class="student-name-target">Перова Владислава Сергеевича</strong>`,
    title: 'ЗАЯВЛЕНИЕ',
    text: `Прошу предоставить мне академический отпуск по семейным обстоятельствам сроком на один календарный год с 01 декабря 2026 года по 30 ноября 2027 года. К заявлению прилагаю подтверждающие документы и справку установленного образца.`,
    signLeft: '19 ноября 2026 г.',
    signRight: `_________ / <span class="student-sign-target">В. С. Перов</span>`
  },
  explanation: {
    header: `Декану факультета компьютерных наук<br>к.т.н., доценту Смирнову А. В.<br>студента гр. ПИ-302<br><strong class="student-name-target">Перова Владислава Сергеевича</strong>`,
    title: 'ОБЪЯСНИТЕЛЬНАЯ ЗАПИСКА',
    text: `Я, студент группы ПИ-302, отсутствовал на практических занятиях по дисциплине «Проектирование информационных систем» 17 и 18 ноября 2026 года по причине внезапного обострения ОРВИ. Медицинская справка из поликлиники №4 прилагается.`,
    signLeft: '19 ноября 2026 г.',
    signRight: `_________ / <span class="student-sign-target">В. С. Перов</span>`
  },
  contract: {
    header: `ДОГОВОР № 26/11-ГТ<br>на оказание консультационных услуг<br>г. Москва`,
    title: 'ПРЕДМЕТ ДОГОВОРА',
    text: `Исполнитель <strong class="student-name-target">Перов Владислав Сергеевич</strong> обязуется по заданию Заказчика оказать комплекс услуг по проверке и приведению нормативно-технической документации в строгое соответствие с требованиями ГОСТ Р 7.0.97-2016, а Заказчик обязуется принять и оплатить данные услуги.`,
    signLeft: 'Исполнитель: В. С. Перов',
    signRight: 'Заказчик: ООО «Инновации»'
  }
};

function initTemplatePlayground() {
  const pills = document.querySelectorAll('.template-pill');
  const nameInput = document.getElementById('playground-name-input');
  const docHeader = document.getElementById('paper-header');
  const docTitle = document.getElementById('paper-title');
  const docBody = document.getElementById('paper-text');
  const docSignLeft = document.getElementById('paper-sign-left');
  const docSignRight = document.getElementById('paper-sign-right');

  let activeType = 'academic';

  function updateDocument() {
    const data = TEMPLATE_DATA[activeType];
    const customName = nameInput && nameInput.value.trim() ? nameInput.value.trim() : 'Перова Владислава Сергеевича';
    
    // Получение инициалов
    const parts = customName.split(' ');
    let shortName = customName;
    if (parts.length >= 2) {
      shortName = `${parts[1][0]}. ${parts.length >= 3 ? parts[2][0] + '. ' : ''}${parts[0]}`;
    }

    if (docHeader) docHeader.innerHTML = data.header;
    if (docTitle) docTitle.textContent = data.title;
    if (docBody) docBody.innerHTML = data.text;
    if (docSignLeft) docSignLeft.textContent = data.signLeft;
    if (docSignRight) docSignRight.innerHTML = data.signRight;

    // Обновляем имя во всех динамических точках
    document.querySelectorAll('.student-name-target').forEach(el => el.textContent = customName);
    document.querySelectorAll('.student-sign-target').forEach(el => el.textContent = shortName);
  }

  pills.forEach(pill => {
    pill.addEventListener('click', () => {
      pills.forEach(p => p.classList.remove('active'));
      pill.classList.add('active');
      activeType = pill.dataset.template || 'academic';
      updateDocument();
    });
  });

  if (nameInput) {
    nameInput.addEventListener('input', updateDocument);
  }

  updateDocument();
}

/* 5. Интерактивные вкладки в макете ПК */
function initMockupTabs() {
  const items = document.querySelectorAll('.mock-side-item');
  const previewSheet = document.querySelector('.mock-doc-sheet');
  if (!items.length || !previewSheet) return;

  const states = {
    templates: {
      title: 'ЗАЯВЛЕНИЕ НА ОТПУСК',
      text: 'Прошу предоставить очередной оплачиваемый отпуск на 14 календарных дней...'
    },
    scan: {
      title: 'РЕЗУЛЬТАТ ОЦИФРОВКИ (OCR)',
      text: '[Распознано с бумажного бланка]: Приказ №14-ОД о назначении ответственных лиц...'
    },
    rules: {
      title: 'ПРОВЕРКА ПО ГОСТ Р 7.0.97',
      text: 'Шрифт: Times New Roman 14 ✓ | Поля: 30/15/20/20 ✓ | Отступ: 1.25 см ✓'
    },
    profile: {
      title: 'КАРТОЧКА СОТРУДНИКА / СТУДЕНТА',
      text: 'Реквизиты сохранены локально. Автоподстановка включена во всех документах.'
    }
  };

  items.forEach(item => {
    item.addEventListener('click', () => {
      items.forEach(i => i.classList.remove('active'));
      item.classList.add('active');

      const mode = item.dataset.tab;
      const state = states[mode];
      if (state) {
        const titleEl = previewSheet.querySelector('.mock-doc-title');
        const bodyEl = previewSheet.querySelector('.mock-doc-body');
        if (titleEl) titleEl.textContent = state.title;
        if (bodyEl) bodyEl.textContent = state.text;
      }
    });
  });
}

/* 6. FAQ Аккордеон */
function initFaqAccordion() {
  const items = document.querySelectorAll('.faq-item');
  items.forEach(item => {
    const question = item.querySelector('.faq-question');
    if (!question) return;

    question.addEventListener('click', () => {
      const isActive = item.classList.contains('active');
      items.forEach(i => i.classList.remove('active'));
      if (!isActive) {
        item.classList.add('active');
      }
    });
  });
}

/* 7. Обработчики скачивания и всплывающие подсказки */
function initDownloadHandlers() {
  const downloadLinks = document.querySelectorAll('a[download]');
  downloadLinks.forEach(link => {
    link.addEventListener('click', (e) => {
      const filename = link.getAttribute('download') || 'GosTek';
      showToast(`Началась загрузка: ${filename}`);
    });
  });
}

/* 8. Копирование контрольной суммы */
function initCopyChecksum() {
  const copyBtn = document.getElementById('copy-hash-btn');
  const hashText = document.getElementById('sha-hash-text');
  if (!copyBtn || !hashText) return;

  copyBtn.addEventListener('click', () => {
    const hash = hashText.innerText.trim();
    navigator.clipboard.writeText(hash).then(() => {
      showToast('Хеш SHA-256 скопирован в буфер!');
      const orig = copyBtn.innerHTML;
      copyBtn.textContent = 'Скопировано ✓';
      setTimeout(() => {
        copyBtn.innerHTML = orig;
      }, 2000);
    });
  });
}

/* Вспомогательный тост */
function showToast(message) {
  let toast = document.querySelector('.toast-notice');
  if (!toast) {
    toast = document.createElement('div');
    toast.className = 'toast-notice';
    document.body.appendChild(toast);
  }

  toast.innerHTML = `
    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#38BDF8" stroke-width="2">
      <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/>
    </svg>
    <span>${message}</span>
  `;

  toast.classList.add('show');
  setTimeout(() => {
    toast.classList.remove('show');
  }, 3500);
}

/* 9. Анимация появления при скролле */
function initScrollAnimations() {
  const cards = document.querySelectorAll('.feature-card, .download-card, .trust-card, .gost-item');
  const observer = new IntersectionObserver((entries) => {
    entries.forEach(entry => {
      if (entry.isIntersecting) {
        entry.target.style.opacity = '1';
        entry.target.style.transform = 'translateY(0)';
        observer.unobserve(entry.target);
      }
    });
  }, { threshold: 0.1 });

  cards.forEach(card => {
    card.style.opacity = '0';
    card.style.transform = 'translateY(24px)';
    card.style.transition = 'opacity 0.6s ease, transform 0.6s cubic-bezier(0.16, 1, 0.3, 1)';
    observer.observe(card);
  });
}

/* 10. Переключение вкладок в галерее скриншотов */
function initGalleryTabs() {
  const tabs = document.querySelectorAll('.gallery-tab-btn');
  const winGallery = document.getElementById('gallery-windows');
  const androidGallery = document.getElementById('gallery-android');
  if (!tabs.length || !winGallery || !androidGallery) return;

  tabs.forEach(tab => {
    tab.addEventListener('click', () => {
      tabs.forEach(t => t.classList.remove('active'));
      tab.classList.add('active');

      const platform = tab.dataset.platform;
      if (platform === 'windows') {
        winGallery.style.display = 'grid';
        androidGallery.style.display = 'none';
      } else {
        winGallery.style.display = 'none';
        androidGallery.style.display = 'grid';
      }
    });
  });
}
document.addEventListener('DOMContentLoaded', initGalleryTabs);
