// פונקציה משותפת להצגת סטטוס הצעת מחיר — משמשת את כל הדפים
function getStatusLabel(status) {
    switch (status) {
        case 'Draft':      return '<span style="color:#3498db;font-weight:600;">טיוטה</span>';
        case 'Processing': return '<span style="color:#e67e22;font-weight:600;">מעבד...</span>';
        case 'Completed':  return '<span style="color:#27ae60;font-weight:600;">הושלם</span>';
        case 'Error':      return '<span style="color:#c0392b;font-weight:600;">שגיאה</span>';
        case 'Sent':       return '<span style="color:#8e44ad;font-weight:600;">נשלח</span>';
        case 'Approved':   return '<span style="color:#27ae60;font-weight:600;">אושר</span>';
        case 'Rejected':   return '<span style="color:#c0392b;font-weight:600;">נדחה</span>';
        case 'Cancelled':  return '<span style="color:#95a5a6;font-weight:600;">בוטל</span>';
        default:           return `<span style="color:#888;font-weight:600;">${status || '-'}</span>`;
    }
}
