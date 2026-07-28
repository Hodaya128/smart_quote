let allUsers = [];
const apiRoot = BASE_URL + '/api/users';
const headers = { 'X-Token': 'your-token-here' };

$(document).ready(function () {
    getUsers();

    $('#searchInput').on('input', function () {
        const query = $(this).val().toLowerCase();
        const filtered = allUsers.filter(u => u.email.toLowerCase().includes(query));
        renderUsers(filtered);
    });

    function getUsers() {
        $.ajax({
            url: apiRoot,
            method: 'GET',
            dataType: 'json',
            headers: headers,
            success: function (data) {
                allUsers = data;
                renderUsers(data);
            },
            error: function (xhr, status, error) {
                console.log('Error loading users:', status, error);
                alert('בעיה בטעינה, נסה שוב מאוחר יותר');
            }
        });
    }

    function renderUsers(data) {
        const usersContainer = $('#usersContainer');
        usersContainer.empty();

        const rows = data.length === 0
            ? `<tr><td colspan="5" class="table-empty">אין משתמשים</td></tr>`
            : data.map(u => `
                <tr>
                    <td>${u.userID}</td>
                    <td><img src="../../images/header/profile.png" class="row-icon" alt=""/> ${u.email}</td>
                    <td>${u.type}</td>
                    <td>${u.createdDate || '-'}</td>
                    <td>
                        <img src="../../images/edit.png" class="icon-edit" data-id="${u.userID}" alt="עריכה" style="cursor:pointer;" />
                        <img src="../../images/trash.png" class="icon-delete" data-id="${u.userID}" alt="מחק" />
                    </td>
                </tr>
            `).join('');

        usersContainer.html(`
            <table class="data-table">
                <thead>
                    <tr>
                        <th>#</th>
                        <th>אימייל</th>
                        <th>תפקיד</th>
                        <th>תאריך יצירה</th>
                        <th></th>
                    </tr>
                </thead>
                <tbody>${rows}</tbody>
            </table>
        `);

        $('.icon-delete').click(function () {
            const id = $(this).data('id');
            if (!confirm('האם אתה בטוח שברצונך למחוק את המשתמש?')) return;
            $.ajax({
                url: apiRoot + '/' + id,
                method: 'DELETE',
                headers: headers,
                success: function () { getUsers(); },
                error: function (xhr, status, error) {
                    console.log('Error deleting:', status, error);
                    alert('שגיאה במחיקה, נסה שוב');
                }
            });
        });

        $('.icon-edit').click(function () {
            const id = $(this).data('id');
            const user = allUsers.find(u => u.userID === id);
            if (!user) return;
            $('#editUserID').val(user.userID);
            $('#editUserEmail').val(user.email);
            $('#editUserType').val(user.type);
            $('#editUserPassword').val('');
            $('#editUserModal').addClass('active');
        });
    }

    $('#closeUserModal, #editUserModal').click(function (e) {
        if (e.target === this) $('#editUserModal').removeClass('active');
    });

    $('#editUserForm').submit(function (e) {
        e.preventDefault();
        const id = $('#editUserID').val();
        const password = $('#editUserPassword').val();

        const updateUser = $.ajax({
            url: apiRoot + '/' + id,
            method: 'PUT',
            contentType: 'application/json',
            headers: headers,
            data: JSON.stringify({ email: $('#editUserEmail').val(), type: $('#editUserType').val() })
        });

        const requests = [updateUser];
        if (password) {
            requests.push($.ajax({
                url: apiRoot + '/' + id + '/password',
                method: 'PUT',
                contentType: 'application/json',
                headers: headers,
                data: JSON.stringify({ password })
            }));
        }

        $.when(...requests)
            .done(function () {
                $('#editUserModal').removeClass('active');
                getUsers();
            })
            .fail(function () { alert('שגיאה בעדכון, נסה שוב'); });
    });
});