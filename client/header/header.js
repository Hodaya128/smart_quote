(function () {
  const MENU = [
    { href: '/pages/home',      label: 'מסך הבית',    img: 'home.png' },
    { href: '/pages/components',  label: 'רכיבים',       img: 'products.png' },
    { href: '/pages/suppliers', label: 'ספקים',         img: 'suppliers.png' },
    { href: '/pages/quotes',    label: 'הצעות מחיר',   img: 'quotes.png' },
    { href: '/pages/customers', label: 'לקוחות',        img: 'customers.png' },
  ];

  const IMG_BASE = '/images/header/'; //תקיית תמונות של header
  const currentPath = window.location.pathname.slice(0, -1) // מביא את המיקום ללא הסלאש האחרון

  const navHTML = MENU.map(link => { //לולאה שמקבלת מערך ומחזירה מערך
    const active = currentPath.includes(link.href) ? ' class="active"' : ''; //בודק אם זה העמוד שהאתר נמצא עליו
    return `<a href="${link.href}"${active}><img src="${IMG_BASE}${link.img}" alt=""/>${link.label}</a>`;
  }).join('');

  const header = document.createElement('header');
  header.id = 'main-header';
  header.innerHTML = `
    <a href="/pages/home" class="header-logo">
        <img src="/images/logo.png" />
    </a>
    <div class="nav">${navHTML}</div> 
    <button class="button-new-quote" onclick="window.location.href='/pages/quotes/new'">+ הצעה חדשה</button>
    <a href="/pages/settings" class="settings-btn">⚙</a>
    <a href="/pages/profile" class="profile">
        <img src="${IMG_BASE}profile.png" alt=""/>
        <span id="header-username">...</span>
    </a>
  `;
//מעלה את ההדר לפני הכל
  document.body.insertBefore(header, document.body.firstChild);
// אחרי שהעמוד עולה


  $(document).ready(function () {
    // מביא את האלמנט של הפרופיל
    const $profileElement = $('#header-username');
    if ($profileElement.length) {
      $profileElement.text(getUserName());
    }

    if (isAdminOrManager()) {  // אם המשתמש מסוג מנהל או אדמין
      const $nav = $('#main-header .nav');  // מביא את ההדר
      if ($nav.length) {       // בודק שהוא קיים
        const usersActive = currentPath.includes('/pages/users') ? ' class="active"' : ''; // בודק אם אנחנו בעמוד של המשתמשים

        $nav.append(
            `<a href="/pages/users"${usersActive}>
          <img src="${IMG_BASE}profile.png" alt=""/>
          משתמשים
        </a>`
        );
      }

    }
  });
})();
